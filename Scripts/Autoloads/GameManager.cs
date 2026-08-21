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
    // Raised on EVERY peer, not just the host: each machine instantiates its own copy of the
    // boss, and each machine's HUD and music react locally. Carries the node so listeners can
    // read its name and health without a lookup.
    [Signal] public delegate void BossSpawnedEventHandler(Node boss);
    [Signal] public delegate void BossDefeatedEventHandler();

    /// <summary>The boss currently alive, or null. Used by the HUD to drive its bar.</summary>
    public Node? ActiveBoss { get; private set; }

    public int CurrentWave { get; private set; }
    public int EnemiesRemaining { get; private set; }
    public bool IsGameRunning { get; private set; }

    private readonly List<Player> _players = new();
    private WaveSpawner? _waveSpawner;

    // StartNextWave/SetEnemiesForWave/OnEnemyKilled only ever run on the host (they're driven
    // by GameManager.StartGame(), itself gated to the server in Arena._Ready()) — GameManager
    // is a plain per-peer autoload, not something Godot syncs on its own, so without this a
    // joining client's own instance would sit at CurrentWave/EnemiesRemaining 0 forever and
    // their HUD (wave banner, enemy counter) would never update even after enemies started
    // appearing for them via WaveSpawner's own RPCs.
    private bool IsHostAuthority => NetworkManager.IsNetworked && Multiplayer.IsServer();

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
        if (IsHostAuthority) Rpc(MethodName.SyncWaveStarted, CurrentWave);
        EmitSignal(SignalName.WaveStarted, CurrentWave);
        // WaveSpawner calls SetEnemiesForWave() before spawning.
        _waveSpawner?.SpawnWave(CurrentWave);
    }

    // Called by WaveSpawner after computing the total count for the wave.
    public void SetEnemiesForWave(int count)
    {
        EnemiesRemaining = count;
        if (IsHostAuthority) Rpc(MethodName.SyncEnemiesRemaining, EnemiesRemaining);
        EmitSignal(SignalName.EnemiesRemainingChanged, EnemiesRemaining);
    }

    public void OnEnemyKilled()
    {
        EnemiesRemaining = Mathf.Max(0, EnemiesRemaining - 1);
        if (IsHostAuthority) Rpc(MethodName.SyncEnemiesRemaining, EnemiesRemaining);
        EmitSignal(SignalName.EnemiesRemainingChanged, EnemiesRemaining);

        if (EnemiesRemaining <= 0)
        {
            EmitSignal(SignalName.WaveCompleted, CurrentWave);
            // processAlways:false so the inter-wave countdown halts while paused.
            GetTree().CreateTimer(3.0, false).Timeout += StartNextWave;
        }
    }

    // ── Client sync (host → everyone else) ──────────────────────────────────────

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    private void SyncWaveStarted(int wave)
    {
        CurrentWave = wave;
        EmitSignal(SignalName.WaveStarted, wave);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    private void SyncEnemiesRemaining(int count)
    {
        EnemiesRemaining = count;
        EmitSignal(SignalName.EnemiesRemainingChanged, count);
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

    public void NotifyBossSpawned(Node boss)
    {
        ActiveBoss = boss;
        EmitSignal(SignalName.BossSpawned, boss);
    }

    public void NotifyBossDefeated(Node boss)
    {
        if (ActiveBoss != boss) return; // ignore a stale death from a previous wave's boss
        ActiveBoss = null;
        EmitSignal(SignalName.BossDefeated);
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
