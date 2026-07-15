using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

/// <summary>
/// Spawns enemies at wave start. Only runs on the host.
/// Enemy nodes are created via Rpc so all peers have the scene node,
/// but Enemy._isHost controls who drives the AI.
/// </summary>
public partial class WaveSpawner : Node
{
    [Export] public NodePath EnemyContainerPath = "../../Enemies";
    [Export] public NodePath SpawnPointsPath = "../../EnemySpawnPoints";

    private Node? _enemyContainer;
    private readonly List<Marker2D> _spawnPoints = new();
    private PackedScene? _enemyScene;
    private bool _isHost;
    private int _spawnIndex;

    public override void _Ready()
    {
        _isHost = !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
        _enemyContainer = GetNode(EnemyContainerPath);
        var spawnRoot = GetNode(SpawnPointsPath);
        foreach (var child in spawnRoot.GetChildren())
            if (child is Marker2D marker)
                _spawnPoints.Add(marker);

        _enemyScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Enemy.tscn");
        GameManager.Instance?.SetWaveSpawner(this);
    }

    public void SpawnWave(int waveNumber, int count)
    {
        if (!_isHost) return;

        // Increase spawn count each wave; spread out spawns slightly.
        for (int i = 0; i < count; i++)
        {
            float delay = i * 0.3f;
            if (Multiplayer.HasMultiplayerPeer())
                Rpc(MethodName.SpawnEnemyRpc, GetNextSpawnPosition(), delay);
            else
                SpawnEnemyRpc(GetNextSpawnPosition(), delay);
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

    private Vector2 GetNextSpawnPosition()
    {
        if (_spawnPoints.Count == 0) return Vector2.Zero;
        var point = _spawnPoints[_spawnIndex % _spawnPoints.Count];
        _spawnIndex++;
        // Jitter to avoid stacking.
        return point.GlobalPosition + new Vector2(
            GD.Randf() * 40f - 20f,
            GD.Randf() * 40f - 20f);
    }
}
