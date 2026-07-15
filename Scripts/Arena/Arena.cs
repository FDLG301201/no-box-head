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

    // Shared obstacle data used by both BuildArena() and BuildNavigationRegion().
    private static readonly (Vector2 Center, Vector2 Size)[] ObstacleData =
    {
        (new(320, 200),  new(80, 80)),
        (new(960, 200),  new(80, 80)),
        (new(320, 520),  new(80, 80)),
        (new(960, 520),  new(80, 80)),
        (new(640, 360),  new(120, 40)),
        (new(640, 280),  new(40, 120)),
    };

    private Node2D?   _players;
    private Node2D?   _enemies;
    private Node2D?   _bullets;
    private Node2D?   _enemySpawnPoints;
    private Node2D?   _playerSpawnPoints;
    private CameraManager? _cameraManager;
    private Control?  _splitScreenRoot;
    private HUD?      _hud;

    private readonly PackedScene _playerScene =
        ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Player.tscn");
    private readonly List<Player> _spawnedPlayers = new();
    private Player? _localPlayer; // reference kept to wire weapon unlocks

    // ── Godot callbacks ───────────────────────────────────────────────────────

    public override void _Ready()
    {
        BuildArena();
        BuildNavigationRegion();
        WireUpChildren();

        if (!Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer())
        {
            SpawnPlayers();
            GameManager.Instance?.StartGame(); // must come after players are spawned
        }
        else
        {
            NetworkManager.Instance.PlayerConnected += _ => { /* handled via RPC */ };
        }

        GameManager.Instance!.GameOver    += OnGameOver;
        GameManager.Instance!.WaveStarted += OnWaveStartedRevive;
        NetworkManager.Instance.PlayerDisconnected += OnPeerDisconnected;
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

    private void BuildArena()
    {
        AddChild(new ColorRect
        {
            Color = new Color(0.18f, 0.18f, 0.18f),
            Size  = new Vector2(ArenaW, ArenaH)
        });

        // Outer walls.
        CreateWall(new Vector2(ArenaW / 2f, -WallT / 2f),        new Vector2(ArenaW + WallT * 2, WallT));
        CreateWall(new Vector2(ArenaW / 2f, ArenaH + WallT / 2f), new Vector2(ArenaW + WallT * 2, WallT));
        CreateWall(new Vector2(-WallT / 2f, ArenaH / 2f),         new Vector2(WallT, ArenaH));
        CreateWall(new Vector2(ArenaW + WallT / 2f, ArenaH / 2f), new Vector2(WallT, ArenaH));

        foreach (var (center, size) in ObstacleData)
            CreateObstacle(center, size);
    }

    private void BuildNavigationRegion()
    {
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

        // Each obstacle carved out as a hole (exact size — no expansion to avoid overlaps).
        foreach (var (center, size) in ObstacleData)
        {
            float hw = size.X / 2f;
            float hh = size.Y / 2f;
            navPoly.AddOutline(new[]
            {
                center + new Vector2(-hw, -hh),
                center + new Vector2(-hw,  hh),
                center + new Vector2( hw,  hh),
                center + new Vector2( hw, -hh),
            });
        }

#pragma warning disable CS0618
        navPoly.MakePolygonsFromOutlines();
#pragma warning restore CS0618
        AddChild(new NavigationRegion2D { NavigationPolygon = navPoly });
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
            Color    = new Color(0.5f, 0.4f, 0.2f),
            Size     = size,
            Position = -size / 2f
        });
        body.Position = center;
        AddChild(body);
    }

    // ── Wiring ────────────────────────────────────────────────────────────────

    private void WireUpChildren()
    {
        _players          = GetOrCreate<Node2D>("Players");
        _enemies          = GetOrCreate<Node2D>("Enemies");
        _bullets          = GetOrCreate<Node2D>("Bullets");
        _enemySpawnPoints = GetOrCreate<Node2D>("EnemySpawnPoints");
        _playerSpawnPoints = GetOrCreate<Node2D>("PlayerSpawnPoints");

        foreach (var pos in EnemySpawnPositions)
            _enemySpawnPoints.AddChild(new Marker2D { Position = pos });
        foreach (var pos in PlayerSpawnPositions)
            _playerSpawnPoints.AddChild(new Marker2D { Position = pos });

        var ws = new WaveSpawner();
        ws.EnemyContainerPath   = _enemies.GetPath();
        ws.SpawnPointsPath      = _enemySpawnPoints.GetPath();
        ws.BulletsContainerPath = _bullets.GetPath();
        AddChild(ws);

        _cameraManager = new CameraManager();
        AddChild(_cameraManager);

        var hudScene = ResourceLoader.Load<PackedScene>("res://Scenes/HUD.tscn");
        if (hudScene != null)
        {
            _hud = hudScene.Instantiate<HUD>();
            AddChild(_hud);
            _hud.PauseCallback = RequestTogglePause;
        }

        _splitScreenRoot = new Control
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        AddChild(_splitScreenRoot);

        SpawnInitialAmmoPacks();
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
            pack.GlobalPosition = pos;
            AddChild(pack);
        }
    }

    private T GetOrCreate<T>(string name) where T : Node, new()
    {
        if (HasNode(name)) return GetNode<T>(name);
        var n = new T { Name = name };
        AddChild(n);
        return n;
    }

    // ── Player spawning ───────────────────────────────────────────────────────

    private void SpawnPlayers()
    {
        if (Multiplayer.HasMultiplayerPeer())
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

        _players!.AddChild(player);
        _spawnedPlayers.Add(player);

        // Give starting weapons (name assigned automatically by AddWeapon).
        var pistol = new Pistol { BulletContainer = _bullets };
        player.AddWeapon(pistol);

        var knife = new Knife { BulletContainer = _bullets };
        player.AddWeapon(knife);

        // In local co-op, both players are on the same machine (no multiplayer peer).
        bool isLocalPlayer = !Multiplayer.HasMultiplayerPeer() ||
                             (Multiplayer.HasMultiplayerPeer() && peerId == Multiplayer.GetUniqueId());

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
                    _hud.SwitchWeaponCallback = player.SwitchToNextWeapon;
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
                        "Shotgun"    => (Weapon)new Shotgun(),
                        "MachineGun" => (Weapon)new MachineGun(),
                        _            => null,
                    };
                    if (w == null || !IsInstanceValid(capturedPlayer)) return;
                    w.BulletContainer = _bullets;
                    capturedPlayer.AddWeapon(w);
                };
            }

            // Joysticks disabled during development — call SetupLocalJoysticks(player) to re-enable.
        }

        CallDeferred(MethodName.RefreshCamera);
    }

    private void RefreshCamera()
    {
        var validPlayers = _spawnedPlayers
            .FindAll(p => IsInstanceValid(p))
            .ConvertAll(p => (Node2D)p);
        _cameraManager?.Setup(validPlayers, _splitScreenRoot!);
    }

    // ── Events ────────────────────────────────────────────────────────────────

    private void OnPeerDisconnected(long peerId)
    {
        var player = _players?.GetNodeOrNull<Player>(
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

    private void OnGameOver()
    {
        int score = ScoreManager.Instance?.Score ?? 0;
        int wave  = GameManager.Instance?.CurrentWave ?? 0;
        _hud?.ShowGameOver(score, wave);
    }
}
