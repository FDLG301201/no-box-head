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

    // Names every spawned enemy identically on every peer. Godot routes RPCs by NodePath, so
    // an enemy left to auto-naming becomes "@CharacterBody2D@147" on the host and some other
    // number on the client — and every position update the host sends for it is then dropped
    // silently, leaving the client's copy frozen where it spawned. The host assigns the id and
    // ships it with the spawn so both sides agree.
    private int                _nextEnemyId;

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
            Emit(MethodName.SpawnEnemyRpc, delay);
        for (int i = 0; i < sprinterCount; i++, delay++)
            Emit(MethodName.SpawnSprinterRpc, delay);
        for (int i = 0; i < demonCount; i++, delay++)
            Emit(MethodName.SpawnDemonRpc, delay);
        for (int i = 0; i < ogreCount; i++, delay++)
            Emit(MethodName.SpawnOgreRpc, delay);
    }

    /// <summary>
    /// Issues one spawn, with the shared id that keeps the node's name identical on every peer.
    /// </summary>
    private void Emit(StringName rpcName, int delay)
    {
        var pos = GetNextSpawnPosition();
        int id  = _nextEnemyId++;
        if (NetworkManager.IsNetworked) Rpc(rpcName, pos, delay * 0.3f, id);
        else                            Call(rpcName, pos, delay * 0.3f, id);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void SpawnEnemyRpc(Vector2 position, float delay, int id)
    {
        if (_enemyScene == null) return;
        async void Deferred()
        {
            if (delay > 0f)
                // processAlways:false so staggered spawns halt while the game is paused.
                await ToSignal(GetTree().CreateTimer(delay, false), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || _enemyContainer == null) return;
            var enemy = _enemyScene.Instantiate<Enemy>();
            enemy.Name = $"E{id}"; // set before AddChild, or Godot auto-names it first
            _enemyContainer.AddChild(enemy);
            enemy.GlobalPosition = position;
        }
        Deferred();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void SpawnDemonRpc(Vector2 position, float delay, int id)
    {
        if (_demonScene == null) return;
        async void Deferred()
        {
            if (delay > 0f)
                // processAlways:false so staggered spawns halt while the game is paused.
                await ToSignal(GetTree().CreateTimer(delay, false), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || _enemyContainer == null) return;
            var demon = _demonScene.Instantiate<Demon>();
            demon.Name = $"E{id}";
            _enemyContainer.AddChild(demon);
            demon.GlobalPosition = position;
            if (_bulletsContainer != null)
                demon.SetProjectileContainer(_bulletsContainer);
        }
        Deferred();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void SpawnSprinterRpc(Vector2 position, float delay, int id)
    {
        if (_sprinterScene == null) return;
        async void Deferred()
        {
            if (delay > 0f)
                // processAlways:false so staggered spawns halt while the game is paused.
                await ToSignal(GetTree().CreateTimer(delay, false), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || _enemyContainer == null) return;
            var sprinter = _sprinterScene.Instantiate<Sprinter>();
            sprinter.Name = $"E{id}";
            _enemyContainer.AddChild(sprinter);
            sprinter.GlobalPosition = position;
        }
        Deferred();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void SpawnOgreRpc(Vector2 position, float delay, int id)
    {
        if (_ogreScene == null) return;
        async void Deferred()
        {
            if (delay > 0f)
                // processAlways:false so staggered spawns halt while the game is paused.
                await ToSignal(GetTree().CreateTimer(delay, false), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || _enemyContainer == null) return;
            var ogre = _ogreScene.Instantiate<Ogre>();
            ogre.Name = $"E{id}";
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
