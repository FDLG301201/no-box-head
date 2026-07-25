using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

/// <summary>
/// Spawns zombies, sprinters (from wave 2), demons (from wave 3), and a mini-boss ogre every
/// OgreWaveInterval waves. Notifies GameManager of the total enemy count before spawning.
/// Only the host drives spawning; clients receive enemies via RPC.
/// </summary>
public partial class WaveSpawner : Node
{
    [Export] public NodePath EnemyContainerPath   = "../../Enemies";
    [Export] public NodePath SpawnPointsPath      = "../../EnemySpawnPoints";
    [Export] public NodePath BulletsContainerPath = "../../Bullets";

    // Mini-boss schedule: debuts on wave 4, returns every 2 waves after that, and every
    // 3rd appearance brings an extra ogre along (waves 8, 14, 20… step the count up).
    private const int OgreFirstWave      = 4;
    private const int OgreWaveInterval   = 2;
    private const int OgreAppearancesPerExtra = 3;

    private Node?              _enemyContainer;
    private Node?              _bulletsContainer;
    private readonly List<Marker2D> _spawnPoints = new();
    private PackedScene?       _enemyScene;
    private PackedScene?       _demonScene;
    private PackedScene?       _sprinterScene;
    private PackedScene?       _ogreScene;
    private bool               _isHost;
    private int                _spawnIndex;

    public override void _Ready()
    {
        _isHost           = !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
        _enemyContainer   = GetNode(EnemyContainerPath);
        _bulletsContainer = GetNode(BulletsContainerPath);

        var spawnRoot = GetNode(SpawnPointsPath);
        foreach (var child in spawnRoot.GetChildren())
            if (child is Marker2D marker)
                _spawnPoints.Add(marker);

        _enemyScene    = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Enemy.tscn");
        _demonScene    = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Demon.tscn");
        _sprinterScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Sprinter.tscn");
        _ogreScene     = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Ogre.tscn");

        GameManager.Instance?.SetWaveSpawner(this);
    }

    public void SpawnWave(int waveNumber)
    {
        if (!_isHost) return;

        int zombieCount   = 3 + waveNumber * 2;
        int demonCount    = waveNumber >= 3 ? (waveNumber - 2) : 0;
        int sprinterCount = waveNumber >= 2 ? 1 + waveNumber / 2 : 0;
        int ogreCount     = OgreCountForWave(waveNumber);
        int total         = zombieCount + demonCount + sprinterCount + ogreCount;

        GameManager.Instance?.SetEnemiesForWave(total);

        int delay = 0;
        for (int i = 0; i < zombieCount; i++, delay++)
        {
            var pos = GetNextSpawnPosition();
            if (Multiplayer.HasMultiplayerPeer()) Rpc(MethodName.SpawnEnemyRpc, pos, delay * 0.3f);
            else SpawnEnemyRpc(pos, delay * 0.3f);
        }
        for (int i = 0; i < sprinterCount; i++, delay++)
        {
            var pos = GetNextSpawnPosition();
            if (Multiplayer.HasMultiplayerPeer()) Rpc(MethodName.SpawnSprinterRpc, pos, delay * 0.3f);
            else SpawnSprinterRpc(pos, delay * 0.3f);
        }
        for (int i = 0; i < demonCount; i++, delay++)
        {
            var pos = GetNextSpawnPosition();
            if (Multiplayer.HasMultiplayerPeer()) Rpc(MethodName.SpawnDemonRpc, pos, delay * 0.3f);
            else SpawnDemonRpc(pos, delay * 0.3f);
        }
        for (int i = 0; i < ogreCount; i++, delay++)
        {
            var pos = GetNextSpawnPosition();
            if (Multiplayer.HasMultiplayerPeer()) Rpc(MethodName.SpawnOgreRpc, pos, delay * 0.3f);
            else SpawnOgreRpc(pos, delay * 0.3f);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void SpawnEnemyRpc(Vector2 position, float delay)
    {
        if (_enemyScene == null) return;
        async void Deferred()
        {
            if (delay > 0f)
                // processAlways:false so staggered spawns halt while the game is paused.
                await ToSignal(GetTree().CreateTimer(delay, false), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || _enemyContainer == null) return;
            var enemy = _enemyScene.Instantiate<Enemy>();
            _enemyContainer.AddChild(enemy);
            enemy.GlobalPosition = position;
        }
        Deferred();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void SpawnDemonRpc(Vector2 position, float delay)
    {
        if (_demonScene == null) return;
        async void Deferred()
        {
            if (delay > 0f)
                // processAlways:false so staggered spawns halt while the game is paused.
                await ToSignal(GetTree().CreateTimer(delay, false), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || _enemyContainer == null) return;
            var demon = _demonScene.Instantiate<Demon>();
            _enemyContainer.AddChild(demon);
            demon.GlobalPosition = position;
            if (_bulletsContainer != null)
                demon.SetProjectileContainer(_bulletsContainer);
        }
        Deferred();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void SpawnSprinterRpc(Vector2 position, float delay)
    {
        if (_sprinterScene == null) return;
        async void Deferred()
        {
            if (delay > 0f)
                // processAlways:false so staggered spawns halt while the game is paused.
                await ToSignal(GetTree().CreateTimer(delay, false), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || _enemyContainer == null) return;
            var sprinter = _sprinterScene.Instantiate<Sprinter>();
            _enemyContainer.AddChild(sprinter);
            sprinter.GlobalPosition = position;
        }
        Deferred();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void SpawnOgreRpc(Vector2 position, float delay)
    {
        if (_ogreScene == null) return;
        async void Deferred()
        {
            if (delay > 0f)
                // processAlways:false so staggered spawns halt while the game is paused.
                await ToSignal(GetTree().CreateTimer(delay, false), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || _enemyContainer == null) return;
            var ogre = _ogreScene.Instantiate<Ogre>();
            _enemyContainer.AddChild(ogre);
            ogre.GlobalPosition = position;
        }
        Deferred();
    }

    // 0 on waves without a mini-boss, otherwise 1 plus one extra per 3 appearances so far.
    private static int OgreCountForWave(int waveNumber)
    {
        if (waveNumber < OgreFirstWave) return 0;
        if ((waveNumber - OgreFirstWave) % OgreWaveInterval != 0) return 0;

        int appearance = (waveNumber - OgreFirstWave) / OgreWaveInterval + 1; // 1-based
        return 1 + appearance / OgreAppearancesPerExtra;
    }

    private Vector2 GetNextSpawnPosition()
    {
        if (_spawnPoints.Count == 0) return Vector2.Zero;
        var point = _spawnPoints[_spawnIndex % _spawnPoints.Count];
        _spawnIndex++;
        return point.GlobalPosition + new Vector2(
            GD.Randf() * 40f - 20f,
            GD.Randf() * 40f - 20f);
    }
}
