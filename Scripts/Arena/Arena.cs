using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

/// <summary>
/// Root script for the gameplay scene.
/// Builds walls/obstacles, the navigation mesh, spawns players, and wires up the camera.
/// </summary>
public partial class Arena : Node2D
{
	private const float ArenaW = 1280f;
	private const float ArenaH = 720f;
	private const float WallT  = 32f;

	private static readonly Vector2[] PlayerSpawnPositions =
	{
		new(120, 120), new(ArenaW - 120, 120),
		new(120, ArenaH - 120), new(ArenaW - 120, ArenaH - 120)
	};

	private static readonly Vector2[] EnemySpawnPositions =
	{
		new(ArenaW / 2f, 60),
		new(ArenaW / 2f, ArenaH - 60),
		new(60, ArenaH / 2f),
		new(ArenaW - 60, ArenaH / 2f),
		new(200, 60), new(ArenaW - 200, 60),
		new(200, ArenaH - 60), new(ArenaW - 200, ArenaH - 60),
	};

	// Obstacle data + theme for the selected arena, resolved in _Ready() and shared by
	// BuildArena() and BuildNavigationRegion().
	private (Vector2 Center, Vector2 Size)[] _obstacleData = System.Array.Empty<(Vector2, Vector2)>();
	private Color _floorColor    = new(0.18f, 0.18f, 0.18f);
	private Color _obstacleColor = new(0.5f, 0.4f, 0.2f);

	// Single Y-sorted parent holding every player and enemy — see WireUpChildren().
	private Node2D?   _entityLayer;
	private Node2D?   _bullets;
	private Node2D?   _obstaclesRuntime;
	private Node2D?   _enemySpawnPoints;
	private Node2D?   _playerSpawnPoints;
	private CameraManager? _cameraManager;
	private Control?  _splitScreenRoot;
	private HUD?      _hud;

	private NavigationRegion2D?  _navRegion;
	private readonly List<Barrel> _barrels = new();

	private readonly PackedScene _playerScene =
		ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Player.tscn");
	private readonly List<Player> _spawnedPlayers = new();
	private Player? _localPlayer; // reference kept to wire weapon unlocks

	// ── Godot callbacks ───────────────────────────────────────────────────────

	public override void _Ready()
	{
		// GameManager is an autoload — it survives a scene reload. Without this, clicking
		// Restart/Play Again (both just call ReloadCurrentScene, bypassing MainMenu's own
		// ResetGame call) left the OLD, now-freed Player instance sitting in GameManager's
		// _players list forever. The new player would register alongside it, so the list
		// never dropped back to zero on death — Game Over silently stopped firing on any
		// playthrough after the first one in the same session.
		GameManager.Instance?.ResetGame();

		ResolveArena();
		BuildArena();
		BuildNavigationRegion();
		WireUpChildren();

		if (!NetworkManager.IsNetworked)
		{
			SpawnPlayers();
			GameManager.Instance?.StartGame(); // must come after players are spawned
		}
		else if (Multiplayer.IsServer())
		{
			// Do NOT start here. Spawning players and wave 1 immediately meant those RPCs went
			// out while the clients were still loading their own Arena, and an RPC to a peer
			// that has not reached the matching node yet is simply dropped — which is exactly
			// why a joining player arrived to an empty world with no enemies and no wave.
			// Wait until every peer says its arena is up (see ReportArenaReadyRpc).
			_peersReady.Add(1); // the host's own arena is ready right now
			CheckAllPeersReady();
		}
		else
		{
			// Tell the host this arena exists and can receive spawn RPCs.
			RpcId(1, MethodName.ReportArenaReadyRpc);
		}

		GameManager.Instance!.GameOver    += OnGameOver;
		GameManager.Instance!.WaveStarted += OnWaveStartedRevive;
		NetworkManager.Instance.PlayerDisconnected += OnPeerDisconnected;
	}

	// GameManager/NetworkManager are autoloads and outlive this Arena across a scene reload
	// (Restart/Play Again both just call ReloadCurrentScene). Left subscribed, the OLD Arena
	// instance would keep reacting — e.g. OnGameOver reaching into a disposed HUD — every time
	// the NEW playthrough's GameManager events fired.
	public override void _ExitTree()
	{
		if (GameManager.Instance != null)
		{
			GameManager.Instance.GameOver    -= OnGameOver;
			GameManager.Instance.WaveStarted -= OnWaveStartedRevive;
		}
		if (NetworkManager.Instance != null)
			NetworkManager.Instance.PlayerDisconnected -= OnPeerDisconnected;
	}

	// ── Pause ─────────────────────────────────────────────────────────────────

	// Called by HUD when the player presses P or the Resume button.
	public void RequestTogglePause()
	{
		bool nowPaused = !GetTree().Paused;
		if (Multiplayer.HasMultiplayerPeer())
			Rpc(MethodName.SyncPauseRpc, nowPaused);
		else
			ApplyPause(nowPaused);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void SyncPauseRpc(bool paused) => ApplyPause(paused);

	private void ApplyPause(bool paused)
	{
		GetTree().Paused = paused;
		_hud?.SetPauseOverlayVisible(paused);
	}

	// ── Arena construction ────────────────────────────────────────────────────

	private void ResolveArena()
	{
		var type = SettingsManager.Instance?.ArenaType ?? ArenaType.Classic;

		// In networked play each machine builds its own arena from its own settings, so a
		// mismatched (or random) choice would desync walls. Force the shared default there.
		if (Multiplayer.HasMultiplayerPeer())
			type = ArenaType.Classic;

		ulong seed = SettingsManager.Instance?.ArenaSeed ?? 0;
		_obstacleData  = ArenaLayouts.GetObstacles(type, seed);
		_floorColor    = ArenaLayouts.FloorColor(type);
		_obstacleColor = ArenaLayouts.ObstacleColor(type);
	}

	private void BuildArena()
	{
		AddChild(new ColorRect
		{
			Color = _floorColor,
			Size  = new Vector2(ArenaW, ArenaH)
		});

		// Added right after the floor so stains sit on the ground but under walls and entities.
		AddChild(new BloodSystem());

		// Outer walls.
		CreateWall(new Vector2(ArenaW / 2f, -WallT / 2f),        new Vector2(ArenaW + WallT * 2, WallT));
		CreateWall(new Vector2(ArenaW / 2f, ArenaH + WallT / 2f), new Vector2(ArenaW + WallT * 2, WallT));
		CreateWall(new Vector2(-WallT / 2f, ArenaH / 2f),         new Vector2(WallT, ArenaH));
		CreateWall(new Vector2(ArenaW + WallT / 2f, ArenaH / 2f), new Vector2(WallT, ArenaH));

		foreach (var (center, size) in _obstacleData)
			CreateObstacle(center, size);
	}

	// Added on every side of every carved obstacle/barrel, so it doubles up on any gap flanked
	// by two carved shapes (e.g. two barrels, or a barrel next to an obstacle): the walkable
	// mesh remaining between them is the physical gap minus 2×NavMargin. At the old 10f, a
	// 30x30 barrel carved a 50x50 hole and a physical gap needed >20px of clearance beyond the
	// shapes themselves before the nav mesh called it reachable — well past what the smallest
	// enemy (Sprinter, body radius 9) or the common case (Enemy, body radius 11) actually need,
	// which is why a single well-placed barrel could wall off a route a zombie would otherwise
	// fit through. 6f keeps a real buffer — still bigger than Sprinter's own radius, so the nav
	// path doesn't get routed close enough to visibly cut a barrel's corner — while cutting the
	// over-carve on each flanked gap from 20px to 12px.
	private const float NavMargin = 6f;

	private void BuildNavigationRegion()
	{
		_navRegion = new NavigationRegion2D();
		AddChild(_navRegion);
		RebuildNavPolygon();
	}

	// Rebuilds the nav mesh from the static obstacles plus any barrels currently standing.
	// Barrels are dynamic (placed/destroyed mid-game), so this reruns whenever the barrel
	// list changes — infrequent enough (player-triggered) that a full rebake is cheap.
	private void RebuildNavPolygon()
	{
		if (_navRegion == null) return;

		var navPoly = new NavigationPolygon();

		// Walkable outer boundary inset from walls.
		float inset = WallT + 4f;
		navPoly.AddOutline(new[]
		{
			new Vector2(inset, inset),
			new Vector2(ArenaW - inset, inset),
			new Vector2(ArenaW - inset, ArenaH - inset),
			new Vector2(inset, ArenaH - inset),
		});

		// Obstacles carved with a margin so the nav mesh never routes enemies along a path
		// that clips into an obstacle's physical shape. Expanded rectangles that OVERLAP
		// (e.g. the two bars of the central cross) must be merged into a single outline first —
		// overlapping outlines break the triangulation and leave enemies stuck at the seam.
		var rects = new List<Vector2[]>();
		foreach (var (center, size) in _obstacleData)
			rects.Add(MakeMarginRect(center, size));
		foreach (var barrel in _barrels)
			if (IsInstanceValid(barrel))
				rects.Add(MakeMarginRect(barrel.GlobalPosition, barrel.Size));

		foreach (var outline in MergeOverlappingRects(rects))
			navPoly.AddOutline(outline);

#pragma warning disable CS0618
		navPoly.MakePolygonsFromOutlines();
#pragma warning restore CS0618
		_navRegion.NavigationPolygon = navPoly;
	}

	private static Vector2[] MakeMarginRect(Vector2 center, Vector2 size)
	{
		float hw = size.X / 2f + NavMargin;
		float hh = size.Y / 2f + NavMargin;
		return new[]
		{
			center + new Vector2(-hw, -hh),
			center + new Vector2( hw, -hh),
			center + new Vector2( hw,  hh),
			center + new Vector2(-hw,  hh),
		};
	}

	// Called by Barrel when placed/destroyed to keep the nav mesh (and therefore enemy
	// pathfinding) in sync with which paths are currently blocked.
	public void RegisterBarrel(Barrel barrel)
	{
		_barrels.Add(barrel);
		RebuildNavPolygon();
	}

	public void UnregisterBarrel(Barrel barrel)
	{
		_barrels.Remove(barrel);
		RebuildNavPolygon();
	}

	// Unions any obstacle rectangles that overlap so the resulting outlines are disjoint.
	// Geometry2D.MergePolygons returns a single polygon when the two overlap (their union),
	// or two polygons when they are separate. We accumulate, restarting after each merge so
	// chains of overlapping shapes collapse into one outline.
	private static List<Vector2[]> MergeOverlappingRects(List<Vector2[]> rects)
	{
		var result = new List<Vector2[]>();
		foreach (var rect in rects)
		{
			var current = rect;
		restart:
			for (int i = 0; i < result.Count; i++)
			{
				var union = Geometry2D.MergePolygons(current, result[i]);
				if (union.Count == 1)
				{
					current = union[0];
					result.RemoveAt(i);
					goto restart;
				}
			}
			result.Add(current);
		}
		return result;
	}

	private void CreateWall(Vector2 center, Vector2 size)
	{
		var body = new StaticBody2D { CollisionLayer = 1 };
		body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = size } });
		body.AddChild(new ColorRect
		{
			Color    = new Color(0.35f, 0.3f, 0.25f),
			Size     = size,
			Position = -size / 2f
		});
		body.Position = center;
		AddChild(body);
	}

	private void CreateObstacle(Vector2 center, Vector2 size)
	{
		var body = new StaticBody2D { CollisionLayer = 1 };
		body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = size } });
		body.AddChild(new ColorRect
		{
			Color    = _obstacleColor,
			Size     = size,
			Position = -size / 2f
		});
		body.Position = center;
		AddChild(body);
	}

	// ── Wiring ────────────────────────────────────────────────────────────────

	private void WireUpChildren()
	{
		// Players and enemies must live directly under ONE Y-sorted node. Godot only merges
		// nodes into a single Y-sort when they are siblings under the sorted parent; separate
		// "Players" and "Enemies" containers both sit at y=0, so they tie in the parent's sort
		// and fall back to scene-tree order — every enemy then draws in front of every player
		// regardless of where they actually are. A zombie standing above the player looked
		// pasted on top of them rather than standing behind them, which is what read as the
		// zombie being glued on. Sorted together, whoever is lower on screen draws in front.
		_entityLayer      = GetOrCreate<Node2D>("Entities");
		_entityLayer.YSortEnabled = true;
		_bullets          = GetOrCreate<Node2D>("Bullets");
		_obstaclesRuntime = GetOrCreate<Node2D>("RuntimeObstacles");
		_enemySpawnPoints = GetOrCreate<Node2D>("EnemySpawnPoints");
		_playerSpawnPoints = GetOrCreate<Node2D>("PlayerSpawnPoints");

		foreach (var pos in EnemySpawnPositions)
			_enemySpawnPoints.AddChild(new Marker2D { Position = pos });
		foreach (var pos in PlayerSpawnPositions)
			_playerSpawnPoints.AddChild(new Marker2D { Position = pos });

		var ws = new WaveSpawner { Name = "WaveSpawner" };
		ws.EnemyContainerPath   = _entityLayer.GetPath();
		ws.SpawnPointsPath      = _enemySpawnPoints.GetPath();
		ws.BulletsContainerPath = _bullets.GetPath();
		AddChild(ws);

		// Explicit names matter here: WaveSpawner's enemy-spawn RPCs are routed by NodePath,
		// which only resolves on other peers if every node along the path is named the same
		// way on every peer. Without this, an auto-generated name like "@WaveSpawner@23" could
		// drift out of sync between the host and a joining client (e.g. if anything upstream
		// created even one extra/fewer sibling node first) and enemy RPCs would silently never
		// reach the client at all — no error, just an empty arena for them.
		_cameraManager = new CameraManager { Name = "CameraManager" };
		AddChild(_cameraManager);

		var hudScene = ResourceLoader.Load<PackedScene>("res://Scenes/HUD.tscn");
		if (hudScene != null)
		{
			_hud = hudScene.Instantiate<HUD>();
			AddChild(_hud);
			_hud.PauseCallback  = RequestTogglePause;
			_hud.ReviveCallback = ReviveAfterRewardedAd;
		}

		// Split-screen views are screen space. As a plain Control child of Arena (a Node2D) the
		// SubViewportContainers belonged to the world canvas: laid out in world coordinates and
		// drawn inside the very world their own viewports render, so they moved and stacked
		// with the scene instead of staying pinned to the display. HUD already gets this right
		// with a CanvasLayer — mirror it, below HUD's layer 10 so the overlay stays on top.
		var splitLayer = new CanvasLayer { Name = "SplitScreenLayer", Layer = 5 };
		AddChild(splitLayer);

		_splitScreenRoot = new Control
		{
			AnchorRight = 1f, AnchorBottom = 1f,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		splitLayer.AddChild(_splitScreenRoot);

		SpawnInitialAmmoPacks();
		SpawnInitialHealthPacks();
	}

	private void SpawnInitialHealthPacks()
	{
		var packScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/HealthPack.tscn");
		if (packScene == null) return;

		var pack = packScene.Instantiate<HealthPack>();
		pack.GlobalPosition = FreeSpotNear(new Vector2(ArenaW / 2f, ArenaH / 2f));
		AddChild(pack);
	}

	private void SpawnInitialAmmoPacks()
	{
		var packScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/AmmoPack.tscn");
		if (packScene == null) return;

		Vector2[] positions = { new(400, 240), new(880, 240), new(400, 480), new(880, 480) };
		foreach (var pos in positions)
		{
			var pack = packScene.Instantiate<AmmoPack>();
			pack.AmmoAmount     = 12;
			pack.GlobalPosition = FreeSpotNear(pos);
			AddChild(pack);
		}
	}

	// Nudges a desired pickup position off any obstacle (arena layouts vary, so a fixed spot
	// can land on a wall). Returns the point if clear, else the nearest free spot around it.
	private Vector2 FreeSpotNear(Vector2 desired)
	{
		const float clearance = 22f; // pickup radius (14) + a little breathing room
		if (IsClearOfObstacles(desired, clearance)) return desired;

		for (int ring = 1; ring <= 8; ring++)
		{
			float radius = ring * 28f;
			for (int i = 0; i < 12; i++)
			{
				var candidate = desired + Vector2.FromAngle(Mathf.Tau * i / 12f) * radius;
				if (candidate.X < 48f || candidate.Y < 48f ||
					candidate.X > ArenaW - 48f || candidate.Y > ArenaH - 48f)
					continue;
				if (IsClearOfObstacles(candidate, clearance)) return candidate;
			}
		}
		return desired; // arena is unusually packed — fall back to the original spot
	}

	private bool IsClearOfObstacles(Vector2 point, float clearance)
	{
		foreach (var (center, size) in _obstacleData)
		{
			float hw = size.X / 2f + clearance;
			float hh = size.Y / 2f + clearance;
			if (Mathf.Abs(point.X - center.X) < hw && Mathf.Abs(point.Y - center.Y) < hh)
				return false;
		}
		return true;
	}

	private T GetOrCreate<T>(string name) where T : Node, new()
	{
		if (HasNode(name)) return GetNode<T>(name);
		var n = new T { Name = name };
		AddChild(n);
		return n;
	}

	// ── Networked start handshake ─────────────────────────────────────────────

	// Peers whose Arena is built and therefore able to receive spawn RPCs. Host-only state.
	private readonly HashSet<long> _peersReady = new();
	private bool _startedNetworkedGame;

	/// <summary>
	/// A client reporting that its Arena is in the tree. Sent to the host only, so that the
	/// host can hold wave 1 until nobody will miss the spawn RPCs.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	private void ReportArenaReadyRpc()
	{
		if (!Multiplayer.IsServer()) return;
		_peersReady.Add(Multiplayer.GetRemoteSenderId());
		CheckAllPeersReady();
	}

	private void CheckAllPeersReady()
	{
		if (_startedNetworkedGame || !Multiplayer.IsServer()) return;

		// Every peer the lobby handed out an index to has to be accounted for; starting on a
		// partial set would leave the stragglers in the same empty world as before.
		foreach (var peerId in NetworkManager.Instance.PeerPlayerIndex.Keys)
			if (!_peersReady.Contains(peerId))
				return;

		_startedNetworkedGame = true;
		SpawnPlayers();
		GameManager.Instance?.StartGame(); // must come after players are spawned
	}

	// ── Player spawning ───────────────────────────────────────────────────────

	private void SpawnPlayers()
	{
		// IsNetworked, not HasMultiplayerPeer(): the latter is true even offline because Godot
		// installs an OfflineMultiplayerPeer by default, so an offline game that skipped
		// MainMenu's Disconnect() took the networked branch, iterated an empty PeerPlayerIndex
		// and spawned nobody at all.
		if (NetworkManager.IsNetworked)
		{
			foreach (var (peerId, idx) in NetworkManager.Instance.PeerPlayerIndex)
				Rpc(MethodName.SpawnPlayerRpc, idx, peerId);
		}
		else
		{
			SpawnPlayerRpc(0, 1);
			if (SettingsManager.Instance?.GameMode == GameMode.LocalCoop)
				SpawnPlayerRpc(1, 1);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	private void SpawnPlayerRpc(int playerIndex, long peerId)
	{
		var player = _playerScene.Instantiate<Player>();
		player.Name      = $"Player{playerIndex}";
		player.PlayerIndex = playerIndex;
		player.SetMultiplayerAuthority((int)peerId);
		player.GlobalPosition = PlayerSpawnPositions[playerIndex % PlayerSpawnPositions.Length];

		_entityLayer!.AddChild(player);
		_spawnedPlayers.Add(player);

		// Give starting weapons (name assigned automatically by AddWeapon).
		var pistol = new Pistol { BulletContainer = _bullets };
		player.AddWeapon(pistol);

		var knife = new Knife { BulletContainer = _bullets };
		player.AddWeapon(knife);

		// In local co-op, both players are on the same machine (no multiplayer peer).
		bool isLocalPlayer = !NetworkManager.IsNetworked || peerId == Multiplayer.GetUniqueId();

		if (isLocalPlayer)
		{
			bool isCoopP2 = SettingsManager.Instance?.GameMode == GameMode.LocalCoop && playerIndex == 1;

			if (!isCoopP2)
			{
				_localPlayer = player;

				player.AmmoChanged   += (cur, res) => _hud?.UpdateAmmo(cur, res);
				player.Reloading     += rel         => _hud?.SetReloading(rel);
				player.HealthChanged += (cur, max)  => _hud?.UpdateHealth(cur, max);
				player.WeaponChanged += name         => _hud?.UpdateWeapon(name);

				if (_hud != null)
				{
					_hud.SwitchWeaponCallback     = player.SwitchToNextWeapon;
					_hud.SwitchWeaponPrevCallback = player.SwitchToPreviousWeapon;
					_hud.BindToPlayer(player, pistol);
				}
			}
			else
			{
				// P2 signals route to the right-side HUD panel.
				player.AmmoChanged   += (cur, res) => _hud?.UpdateAmmoP2(cur, res);
				player.Reloading     += rel         => _hud?.SetReloadingP2(rel);
				player.HealthChanged += (cur, max)  => _hud?.UpdateHealthP2(cur, max);
				player.WeaponChanged += name         => _hud?.UpdateWeaponP2(name);

				_hud?.BindToPlayerP2(player, pistol);
			}

			// Each local player gets their own weapon instances when weapons unlock.
			if (ScoreManager.Instance != null)
			{
				var capturedPlayer = player;
				ScoreManager.Instance.WeaponUnlocked += weaponName =>
				{
					Weapon? w = weaponName switch
					{
						"Shotgun"     => (Weapon)new Shotgun(),
						"MachineGun"  => (Weapon)new MachineGun(),
						"Grenade"     => (Weapon)new GrenadeLauncher(),
						"Barrel"      => (Weapon)new BarrelWeapon(),
						"Railgun"     => (Weapon)new Railgun(),
						"FlakShotgun" => (Weapon)new FlakShotgun(),
						"Chainsaw"    => (Weapon)new Chainsaw(),
						"RocketLauncher" => (Weapon)new RocketLauncher(),
						"MineWeapon"     => (Weapon)new MineWeapon(),
						"Flamethrower"   => (Weapon)new Flamethrower(),
						"CryoGun"        => (Weapon)new CryoGun(),
						"TurretWeapon"   => (Weapon)new TurretWeapon(),
						_             => null,
					};
					if (w == null || !IsInstanceValid(capturedPlayer)) return;
					w.BulletContainer = _bullets;
					if (w is BarrelWeapon barrelWeapon)
					{
						barrelWeapon.ArenaRef          = this;
						barrelWeapon.ObstacleContainer = _obstaclesRuntime;
					}
					capturedPlayer.AddWeapon(w);
				};
			}

			// Touch controls only: on desktop/editor builds Player reads WASD/mouse directly
			// and these are never created, so no virtual joystick appears outside mobile.
			if (Platform.IsMobile)
				SetupLocalJoysticks(player);
		}

		CallDeferred(MethodName.RefreshCamera);
	}

	private void SetupLocalJoysticks(Player player)
	{
		var joystickScene = ResourceLoader.Load<PackedScene>("res://Scenes/UI/VirtualJoystick.tscn");
		if (joystickScene == null || _hud == null) return;

		// Only the move stick is unconditional. Whether aiming uses a second stick or a single
		// FIRE button depends on the selected aim mode, so the HUD builds (and can later
		// rebuild) that half on its own — see HUD.AddTouchControls.
		var move = joystickScene.Instantiate<VirtualJoystick>();
		player.SetJoysticks(move, null);

		bool isCoop = SettingsManager.Instance?.GameMode == GameMode.LocalCoop;
		_hud.AddTouchControls(move, player, isCoop);
	}

	private void RefreshCamera()
	{
		var validPlayers = _spawnedPlayers
			.FindAll(p => IsInstanceValid(p))
			.ConvertAll(p => (Node2D)p);

		// Online each peer renders its own screen, so the camera must follow that peer's
		// own player. Passing null keeps the offline behaviour (shared or split-screen).
		Node2D? follow = NetworkManager.IsNetworked && IsInstanceValid(_localPlayer)
			? _localPlayer
			: null;
		_cameraManager?.Setup(validPlayers, _splitScreenRoot!, follow);
	}

	// ── Events ────────────────────────────────────────────────────────────────

	private void OnPeerDisconnected(long peerId)
	{
		var player = _entityLayer?.GetNodeOrNull<Player>(
			$"Player{NetworkManager.Instance.PeerPlayerIndex.GetValueOrDefault(peerId, -1)}");
		if (player == null) return;
		_cameraManager?.RemovePlayer(player);
		_spawnedPlayers.Remove(player);
		player.QueueFree();
	}

	private void OnWaveStartedRevive(int _)
	{
		foreach (var player in _spawnedPlayers)
		{
			if (!IsInstanceValid(player) || player.IsAlive) continue;
			player.Revive(PlayerSpawnPositions[player.PlayerIndex % PlayerSpawnPositions.Length]);
		}
	}

	/// <summary>
	/// Puts the run back on its feet after a rewarded ad was watched to completion. Reuses the
	/// same Player.Revive that OnWaveStartedRevive uses, so a revived player is restored exactly
	/// the way the game already knows how to restore one — full health, back at their spawn, and
	/// re-registered with GameManager so enemies target them again.
	///
	/// The wave in progress is deliberately left running. Dropping the player back into the
	/// horde that just killed them is the point: the ad buys a second chance at THIS wave, not a
	/// free skip to the next one.
	/// </summary>
	private void ReviveAfterRewardedAd()
	{
		foreach (var player in _spawnedPlayers)
		{
			if (!IsInstanceValid(player) || player.IsAlive) continue;
			player.Revive(PlayerSpawnPositions[player.PlayerIndex % PlayerSpawnPositions.Length]);
		}
		// GameManager stopped the run when the last player died; without this the wave spawner
		// and win/lose checks stay switched off and the revived player wanders an inert arena.
		GameManager.Instance?.ResumeAfterRevive();
	}

	private void OnGameOver()
	{
		int score = ScoreManager.Instance?.Score ?? 0;
		int wave  = GameManager.Instance?.CurrentWave ?? 0;
		_hud?.ShowGameOver(score, wave);
	}
}
