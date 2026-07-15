using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

/// <summary>
/// Spawns zombies and (from wave 3) demons at the start of each wave.
/// Notifies GameManager of the total enemy count before spawning.
/// Only the host drives spawning; clients receive enemies via RPC.
/// </summary>
public partial class WaveSpawner : Node
{
    [Export] public NodePath EnemyContainerPath   = "../../Enemies";
    [Export] public NodePath SpawnPointsPath      = "../../EnemySpawnPoints";
    [Export] public NodePath BulletsContainerPath = "../../Bullets";

    private Node?              _enemyContainer;
    private Node?              _bulletsContainer;
    private readonly List<Marker2D> _spawnPoints = new();
    private PackedScene?       _enemyScene;
    private PackedScene?       _demonScene;
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

        _enemyScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Enemy.tscn");
        _demonScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Demon.tscn");

        GameManager.Instance?.SetWaveSpawner(this);
    }

    public void SpawnWave(int waveNumber)
    {
        if (!_isHost) return;

        int zombieCount = 3 + waveNumber * 2;
        int demonCount  = waveNumber >= 3 ? (waveNumber - 2) : 0;
        int total       = zombieCount + demonCount;

        GameManager.Instance?.SetEnemiesForWave(total);

        int delay = 0;
        for (int i = 0; i < zombieCount; i++, delay++)
        {
            var pos = GetNextSpawnPosition();
            if (Multiplayer.HasMultiplayerPeer()) Rpc(MethodName.SpawnEnemyRpc, pos, delay * 0.3f);
            else SpawnEnemyRpc(pos, delay * 0.3f);
        }
        for (int i = 0; i < demonCount; i++, delay++)
        {
            var pos = GetNextSpawnPosition();
            if (Multiplayer.HasMultiplayerPeer()) Rpc(MethodName.SpawnDemonRpc, pos, delay * 0.3f);
            else SpawnDemonRpc(pos, delay * 0.3f);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void SpawnEnemyRpc(Vector2 position, float delay)
    {
        if (_enemyScene == null) return;
        async void Deferred()
        {
            if (delay > 0f)
                await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
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
                await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || _enemyContainer == null) return;
            var demon = _demonScene.Instantiate<Demon>();
            _enemyContainer.AddChild(demon);
            demon.GlobalPosition = position;
            if (_bulletsContainer != null)
                demon.SetProjectileContainer(_bulletsContainer);
        }
        Deferred();
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
