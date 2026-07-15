using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

public partial class ScoreManager : Node
{
    public static ScoreManager Instance { get; private set; } = null!;

    public int Score { get; private set; }
    public float Multiplier { get; private set; } = 1f;

    private float _streakTimer;
    private int _streakCount;

    private const float StreakWindow = 3f;
    private const float MaxMultiplier = 4f;

    // score threshold → weapon id (must match Arena.cs WeaponUnlocked switch)
    private static readonly (int Threshold, string WeaponId)[] Unlocks =
    {
        (500,  "Shotgun"),
        (1500, "MachineGun"),
    };
    // Maps internal id → WeaponName (for ammo pack typing)
    private static readonly System.Collections.Generic.Dictionary<string, string> WeaponIdToName = new()
    {
        { "Shotgun",    "Shotgun"     },
        { "MachineGun", "Machine Gun" },
    };
    private readonly HashSet<string> _unlockedWeapons = new();

    [Signal] public delegate void ScoreChangedEventHandler(int score, float multiplier);
    [Signal] public delegate void WeaponUnlockedEventHandler(string weaponName);

    public override void _Ready() => Instance = this;

    public override void _Process(double delta)
    {
        if (_streakTimer <= 0f) return;
        _streakTimer -= (float)delta;
        if (_streakTimer <= 0f)
        {
            _streakCount = 0;
            Multiplier = 1f;
            EmitSignal(SignalName.ScoreChanged, Score, Multiplier);
        }
    }

    // Call this every time an enemy is killed.
    public void RegisterKill(int basePoints)
    {
        _streakCount++;
        _streakTimer = StreakWindow;
        // x1.0, x1.5, x2.0, x2.5, … capped at MaxMultiplier
        Multiplier = Mathf.Min(1f + (_streakCount - 1) * 0.5f, MaxMultiplier);
        Score += Mathf.RoundToInt(basePoints * Multiplier);
        EmitSignal(SignalName.ScoreChanged, Score, Multiplier);
        CheckUnlocks();
    }

    private void CheckUnlocks()
    {
        foreach (var (threshold, weaponId) in Unlocks)
            if (Score >= threshold && _unlockedWeapons.Add(weaponId))
                EmitSignal(SignalName.WeaponUnlocked, weaponId);
    }

    // Returns a WeaponName picked randomly from unlocked weapon types (for ammo drops).
    public string GetRandomUnlockedAmmoType()
    {
        var available = new System.Collections.Generic.List<string> { "Pistol" };
        foreach (var id in _unlockedWeapons)
            if (WeaponIdToName.TryGetValue(id, out var name)) available.Add(name);
        return available[(int)(GD.Randi() % (uint)available.Count)];
    }

    public void Reset()
    {
        Score = 0;
        Multiplier = 1f;
        _streakCount = 0;
        _streakTimer = 0f;
        _unlockedWeapons.Clear();
        EmitSignal(SignalName.ScoreChanged, Score, Multiplier);
    }
}
