using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

/// <summary>
/// Root script for the gameplay scene.
/// Builds the arena walls/obstacles, spawns players, and wires up the camera.
/// </summary>
public partial class Arena : Node2D
{
    // Arena dimensions (inner playable area, walls add 32 px on each side).
    private const float ArenaW = 1280f;
    private const float ArenaH = 720f;
    private const float WallT = 32f;

    // Player spawn positions (corners, inset from walls).
    private static readonly Vector2[] PlayerSpawnPositions =
    {
        new(120, 120), new(ArenaW - 120, 120),
        new(120, ArenaH - 120), new(ArenaW - 120, ArenaH - 120)
    };

    // Enemy spawn positions along the inside of the walls.
    private static readonly Vector2[] EnemySpawnPositions =
    {
        new(ArenaW / 2f, 60),
        new(ArenaW / 2f, ArenaH - 60),
        new(60,           ArenaH / 2f),
        new(ArenaW - 60,  ArenaH / 2f),
        new(200, 60), new(ArenaW - 200, 60),
        new(200, ArenaH - 60), new(ArenaW - 200, ArenaH - 60),
    };

    private Node2D? _players;
    private Node2D? _enemies;
    private Node2D? _bullets;
    private Node2D? _enemySpawnPoints;
    private Node2D? _playerSpawnPoints;
    private CameraManager? _cameraManager;
    private Control? _splitScreenRoot;
    private HUD? _hud;

    private readonly PackedScene _playerScene =
        ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Player.tscn");
    private readonly List<Player> _spawnedPlayers = new();

    // ── Godot callbacks ───────────────────────────────────────────────────────

    public override void _Ready()
    {
        BuildArena();
        WireUpChildren();

        if (!Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer())
        {
            SpawnPlayers();
            GameManager.Instance?.StartGame();
        }
        else
        {
            // Clients wait for the host to call SpawnPlayerRpc via RPC.
            NetworkManager.Instance.PlayerConnected += _ => { /* handled via RPC */ };
        }

        GameManager.Instance!.GameOver += OnGameOver;
        NetworkManager.Instance.PlayerDisconnected += OnPeerDisconnected;
    }

    public override void _Input(InputEvent ev)
    {
        if (ev.IsActionPressed("reload"))
        {
            foreach (var p in _spawnedPlayers)
                if (!Multiplayer.HasMultiplayerPeer() || p.IsMultiplayerAuthority())
                    p.GetNodeOrNull<Weapon>("Weapon")?.RequestReload();
        }
    }

    // ── Arena construction ────────────────────────────────────────────────────

    private void BuildArena()
    {
        // Floor background.
        var floor = new ColorRect
        {
            Color = new Color(0.18f, 0.18f, 0.18f),
            Size = new Vector2(ArenaW, ArenaH)
        };
        AddChild(floor);

        // Walls.
        CreateWall(new Vector2(ArenaW / 2f, -WallT / 2f), new Vector2(ArenaW + WallT * 2, WallT));
        CreateWall(new Vector2(ArenaW / 2f, ArenaH + WallT / 2f), new Vector2(ArenaW + WallT * 2, WallT));
        CreateWall(new Vector2(-WallT / 2f, ArenaH / 2f), new Vector2(WallT, ArenaH));
        CreateWall(new Vector2(ArenaW + WallT / 2f, ArenaH / 2f), new Vector2(WallT, ArenaH));

        // Internal obstacles for cover.
        CreateObstacle(new Vector2(320, 200), new Vector2(80, 80));
        CreateObstacle(new Vector2(960, 200), new Vector2(80, 80));
        CreateObstacle(new Vector2(320, 520), new Vector2(80, 80));
        CreateObstacle(new Vector2(960, 520), new Vector2(80, 80));
        CreateObstacle(new Vector2(640, 360), new Vector2(120, 40));
        CreateObstacle(new Vector2(640, 280), new Vector2(40, 120));
    }

    private void CreateWall(Vector2 center, Vector2 size)
    {
        var body = new StaticBody2D();
        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = size }
        };
        body.AddChild(shape);

        var visual = new ColorRect
        {
            Color = new Color(0.35f, 0.3f, 0.25f),
            Size = size,
            Position = -size / 2f
        };
        body.AddChild(visual);
        body.Position = center;
        body.CollisionLayer = 1;
        AddChild(body);
    }

    private void CreateObstacle(Vector2 center, Vector2 size)
    {
        var body = new StaticBody2D();
        body.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = size }
        });
        body.AddChild(new ColorRect
        {
            Color = new Color(0.5f, 0.4f, 0.2f),
            Size = size,
            Position = -size / 2f
        });
        body.Position = center;
        body.CollisionLayer = 1;
        AddChild(body);
    }

    // ── Wiring ────────────────────────────────────────────────────────────────

    private void WireUpChildren()
    {
        _players = GetOrCreate<Node2D>("Players");
        _enemies = GetOrCreate<Node2D>("Enemies");
        _bullets = GetOrCreate<Node2D>("Bullets");

        _enemySpawnPoints = GetOrCreate<Node2D>("EnemySpawnPoints");
        _playerSpawnPoints = GetOrCreate<Node2D>("PlayerSpawnPoints");

        // Create spawn point markers.
        foreach (var pos in EnemySpawnPositions)
        {
            var m = new Marker2D { Position = pos };
            _enemySpawnPoints.AddChild(m);
        }
        foreach (var pos in PlayerSpawnPositions)
        {
            var m = new Marker2D { Position = pos };
            _playerSpawnPoints.AddChild(m);
        }

        // WaveSpawner.
        var ws = new WaveSpawner();
        ws.EnemyContainerPath = _enemies.GetPath();
        ws.SpawnPointsPath = _enemySpawnPoints.GetPath();
        AddChild(ws);

        // CameraManager.
        _cameraManager = new CameraManager();
        AddChild(_cameraManager);

        // HUD canvas layer.
        var hudScene = ResourceLoader.Load<PackedScene>("res://Scenes/HUD.tscn");
        if (hudScene != null)
        {
            _hud = hudScene.Instantiate<HUD>();
            AddChild(_hud);
        }

        // Split-screen root (invisible Control covering the full viewport).
        _splitScreenRoot = new Control
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        AddChild(_splitScreenRoot);
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
            // Multiplayer: host spawns all connected players via RPC.
            foreach (var (peerId, idx) in NetworkManager.Instance.PeerPlayerIndex)
                Rpc(MethodName.SpawnPlayerRpc, idx, peerId);
        }
        else
        {
            // Single player.
            SpawnPlayerRpc(0, 1);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void SpawnPlayerRpc(int playerIndex, long peerId)
    {
        var player = _playerScene.Instantiate<Player>();
        player.Name = $"Player{playerIndex}";
        player.PlayerIndex = playerIndex;
        player.SetMultiplayerAuthority((int)peerId);
        player.GlobalPosition = PlayerSpawnPositions[playerIndex % PlayerSpawnPositions.Length];

        _players!.AddChild(player);
        _spawnedPlayers.Add(player);

        // Give weapon.
        var pistol = new Pistol { Name = "Weapon" };
        player.SetWeapon(pistol);
        pistol.AmmoChanged += (cur, max) => _hud?.UpdateAmmo(cur, max);
        pistol.Reloading += isReloading => _hud?.SetReloading(isReloading);

        // If this is our own player, setup HUD and joysticks.
        // Evaluate HasMultiplayerPeer() FIRST so GetUniqueId() is never called without a peer.
        bool isLocalPlayer = (!Multiplayer.HasMultiplayerPeer() && playerIndex == 0) ||
                             (Multiplayer.HasMultiplayerPeer() && peerId == Multiplayer.GetUniqueId());
        if (isLocalPlayer)
        {
            _hud?.BindToPlayer(player, pistol);
            player.HealthChanged += (cur, max) => _hud?.UpdateHealth(cur, max);
            SetupLocalJoysticks(player);
        }

        // Setup camera after all players are spawned.
        CallDeferred(MethodName.RefreshCamera);
    }

    private void SetupLocalJoysticks(Player player)
    {
        var joystickScene = ResourceLoader.Load<PackedScene>("res://Scenes/UI/VirtualJoystick.tscn");
        if (joystickScene == null) return;

        // Bottom-left: move joystick.
        // Anchors: left=0, right=0 (left edge), top=1, bottom=1 (bottom edge).
        // Offsets define a 150x150 box starting 30px from the corner.
        var moveJs = joystickScene.Instantiate<VirtualJoystick>();
        moveJs.AnchorLeft   = 0f; moveJs.AnchorRight  = 0f;
        moveJs.AnchorTop    = 1f; moveJs.AnchorBottom = 1f;
        moveJs.OffsetLeft   =  30f; moveJs.OffsetRight  =  180f;
        moveJs.OffsetTop    = -180f; moveJs.OffsetBottom = -30f;

        // Bottom-right: aim joystick.
        var aimJs = joystickScene.Instantiate<VirtualJoystick>();
        aimJs.AnchorLeft   = 1f; aimJs.AnchorRight  = 1f;
        aimJs.AnchorTop    = 1f; aimJs.AnchorBottom = 1f;
        aimJs.OffsetLeft   = -180f; aimJs.OffsetRight  = -30f;
        aimJs.OffsetTop    = -180f; aimJs.OffsetBottom = -30f;

        _hud?.AddJoysticks(moveJs, aimJs);
        player.SetJoysticks(moveJs, aimJs);
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
        // Find and remove the disconnected player.
        var player = _players?.GetNodeOrNull<Player>($"Player{NetworkManager.Instance.PeerPlayerIndex.GetValueOrDefault(peerId, -1)}");
        if (player != null)
        {
            _cameraManager?.RemovePlayer(player);
            _spawnedPlayers.Remove(player);
            player.QueueFree();
        }
    }

    private void OnGameOver()
    {
        GetTree().CreateTimer(2.0).Timeout += () =>
            GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
    }
}
