using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; } = null!;

    [Signal] public delegate void WaveStartedEventHandler(int waveNumber);
    [Signal] public delegate void WaveCompletedEventHandler(int waveNumber);
    [Signal] public delegate void EnemiesRemainingChangedEventHandler(int count);
    [Signal] public delegate void GameOverEventHandler();

    public int CurrentWave { get; private set; }
    public int EnemiesRemaining { get; private set; }
    public bool IsGameRunning { get; private set; }

    private readonly List<Player> _players = new();
    private WaveSpawner? _waveSpawner;

    public override void _Ready() => Instance = this;

    public void RegisterPlayer(Player player)
    {
        if (!_players.Contains(player))
            _players.Add(player);
    }

    public void UnregisterPlayer(Player player) => _players.Remove(player);

    public void SetWaveSpawner(WaveSpawner spawner) => _waveSpawner = spawner;

    public void StartGame()
    {
        IsGameRunning = true;
        CurrentWave = 0;
        ScoreManager.Instance?.Reset();
        CallDeferred(MethodName.StartNextWave);
    }

    public void StartNextWave()
    {
        CurrentWave++;
        ScoreManager.Instance?.CheckWaveUnlocks(CurrentWave);
        EmitSignal(SignalName.WaveStarted, CurrentWave);
        // WaveSpawner calls SetEnemiesForWave() before spawning.
        _waveSpawner?.SpawnWave(CurrentWave);
    }

    // Called by WaveSpawner after computing the total count for the wave.
    public void SetEnemiesForWave(int count)
    {
        EnemiesRemaining = count;
        EmitSignal(SignalName.EnemiesRemainingChanged, EnemiesRemaining);
    }

    public void OnEnemyKilled()
    {
        EnemiesRemaining = Mathf.Max(0, EnemiesRemaining - 1);
        EmitSignal(SignalName.EnemiesRemainingChanged, EnemiesRemaining);

        if (EnemiesRemaining <= 0)
        {
            EmitSignal(SignalName.WaveCompleted, CurrentWave);
            GetTree().CreateTimer(3.0).Timeout += StartNextWave;
        }
    }

    public void OnPlayerDied(Player player)
    {
        UnregisterPlayer(player);
        if (_players.Count == 0)
        {
            IsGameRunning = false;
            EmitSignal(SignalName.GameOver);
        }
    }

    public void ResetGame()
    {
        CurrentWave = 0;
        EnemiesRemaining = 0;
        IsGameRunning = false;
        _players.Clear();
        _waveSpawner = null;
    }

    // Returns the nearest active player to a world position (used by enemy AI).
    public Player? GetNearestPlayer(Vector2 fromPosition)
    {
        Player? nearest = null;
        float minDist = float.MaxValue;
        foreach (var p in _players)
        {
            if (!IsInstanceValid(p)) continue;
            float dist = fromPosition.DistanceTo(p.GlobalPosition);
            if (dist < minDist) { minDist = dist; nearest = p; }
        }
        return nearest;
    }

}
