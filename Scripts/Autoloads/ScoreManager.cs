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
        (300,  "Barrel"),
        (500,  "Shotgun"),
        (900,  "Chainsaw"),
        (1500, "MachineGun"),
        (2200, "Railgun"),
        (2800, "FlakShotgun"),
        (3000, "Grenade"),
        (3600, "MineWeapon"),
        (4200, "RocketLauncher"),
        (4800, "Flamethrower"),
        (5400, "CryoGun"),
        (6000, "TurretWeapon"),
    };
    // Maps internal id → WeaponName (for ammo pack typing).
    private static readonly System.Collections.Generic.Dictionary<string, string> WeaponIdToName = new()
    {
        { "Shotgun",     "Shotgun"      },
        { "MachineGun",  "Machine Gun"  },
        { "Barrel",      "Barrel"       },
        { "Grenade",     "Grenade"      },
        { "Railgun",     "Railgun"      },
        { "FlakShotgun", "Flak Shotgun" },
        { "MineWeapon",     "Proximity Mine"  },
        { "RocketLauncher", "Rocket Launcher" },
        { "Flamethrower",   "Flamethrower"    },
        { "CryoGun",        "Cryo Gun"        },
        { "TurretWeapon",   "Turret"          },
        // Chainsaw has infinite ammo (like Knife) — deliberately absent, no ammo pack for it.
    };

    // Relative drop weight per ammo type when a pack spawns. Barrel drops at the same rate as
    // regular weapons; Grenade ammo stays intentionally rare even after it's unlocked.
    private static readonly System.Collections.Generic.Dictionary<string, float> AmmoDropWeight = new()
    {
        { "Pistol",       1f    },
        { "Shotgun",      1f    },
        { "Machine Gun",  1f    },
        { "Barrel",       1f    },
        { "Grenade",      0.12f },
        { "Railgun",      0.3f  }, // high-damage piercing shot — kept scarcer than pistol/shotgun
        { "Flak Shotgun", 0.6f  },
        { "Proximity Mine", 0.4f  }, // scarcer than the common weapons, more common than Grenade
        { "Rocket Launcher", 0.35f },
        { "Flamethrower",   0.5f  },
        { "Cryo Gun",       0.4f  },
        { "Turret",         0.3f  }, // powerful and autonomous — kept scarcest of all
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

    // Returns a WeaponName picked from unlocked weapon types (for ammo drops), weighted so
    // rare ammo (Grenade) shows up far less often than the rest.
    public string GetRandomUnlockedAmmoType()
    {
        var available = new System.Collections.Generic.List<string> { "Pistol" };
        foreach (var id in _unlockedWeapons)
            if (WeaponIdToName.TryGetValue(id, out var name)) available.Add(name);

        float totalWeight = 0f;
        foreach (var name in available)
            totalWeight += AmmoDropWeight.GetValueOrDefault(name, 1f);

        float roll = GD.Randf() * totalWeight;
        float cumulative = 0f;
        foreach (var name in available)
        {
            cumulative += AmmoDropWeight.GetValueOrDefault(name, 1f);
            if (roll <= cumulative) return name;
        }
        return available[^1];
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
