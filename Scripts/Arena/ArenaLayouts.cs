using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

/// <summary>
/// Available arenas. Handcrafted layouts are deterministic (identical on every machine,
/// so they are safe in networked play); Random is generated from a seed.
/// </summary>
public enum ArenaType { Classic, Pillars, Corridors, Random }

/// <summary>
/// Provides obstacle layouts and theme colours for each arena. Kept separate from Arena.cs
/// so new worlds can be added here without touching the build/nav logic.
/// </summary>
public static class ArenaLayouts
{
    public const float ArenaW = 1280f;
    public const float ArenaH = 720f;

    // Player + enemy spawn points the random generator must not bury under an obstacle.
    private static readonly Vector2[] SpawnKeepClear =
    {
        // Player corners.
        new(120, 120), new(ArenaW - 120, 120),
        new(120, ArenaH - 120), new(ArenaW - 120, ArenaH - 120),
        // Enemy edge / near-corner spawns.
        new(ArenaW / 2f, 60), new(ArenaW / 2f, ArenaH - 60),
        new(60, ArenaH / 2f), new(ArenaW - 60, ArenaH / 2f),
        new(200, 60), new(ArenaW - 200, 60),
        new(200, ArenaH - 60), new(ArenaW - 200, ArenaH - 60),
    };

    public static string DisplayName(ArenaType type) => type switch
    {
        ArenaType.Pillars   => "Pillars",
        ArenaType.Corridors => "Corridors",
        ArenaType.Random    => "Random",
        _                   => "Classic",
    };

    public static Color FloorColor(ArenaType type) => type switch
    {
        ArenaType.Pillars   => new Color(0.12f, 0.15f, 0.18f),
        ArenaType.Corridors => new Color(0.16f, 0.13f, 0.16f),
        ArenaType.Random    => new Color(0.14f, 0.17f, 0.13f),
        _                   => new Color(0.18f, 0.18f, 0.18f),
    };

    public static Color ObstacleColor(ArenaType type) => type switch
    {
        ArenaType.Pillars   => new Color(0.30f, 0.50f, 0.60f),
        ArenaType.Corridors => new Color(0.52f, 0.32f, 0.52f),
        ArenaType.Random    => new Color(0.42f, 0.52f, 0.26f),
        _                   => new Color(0.50f, 0.40f, 0.20f),
    };

    public static (Vector2 Center, Vector2 Size)[] GetObstacles(ArenaType type, ulong seed) => type switch
    {
        ArenaType.Pillars   => Pillars,
        ArenaType.Corridors => Corridors,
        ArenaType.Random    => GenerateRandom(seed),
        _                   => Classic,
    };

    // ── Handcrafted layouts ─────────────────────────────────────────────────────

    private static readonly (Vector2, Vector2)[] Classic =
    {
        (new(320, 200), new(80, 80)),
        (new(960, 200), new(80, 80)),
        (new(320, 520), new(80, 80)),
        (new(960, 520), new(80, 80)),
        (new(640, 360), new(120, 40)),
        (new(640, 280), new(40, 120)),
    };

    private static readonly (Vector2, Vector2)[] Pillars =
    {
        (new(400, 240), new(50, 50)), (new(640, 240), new(50, 50)), (new(880, 240), new(50, 50)),
        (new(400, 480), new(50, 50)), (new(640, 480), new(50, 50)), (new(880, 480), new(50, 50)),
        (new(520, 360), new(44, 44)), (new(760, 360), new(44, 44)),
    };

    private static readonly (Vector2, Vector2)[] Corridors =
    {
        (new(400, 360), new(44, 200)), // left vertical wall
        (new(880, 360), new(44, 200)), // right vertical wall
        (new(640, 180), new(200, 44)), // top horizontal wall
        (new(640, 540), new(200, 44)), // bottom horizontal wall
        (new(640, 360), new(60, 60)),  // centre block
    };

    // ── Random generation ───────────────────────────────────────────────────────

    private static (Vector2, Vector2)[] GenerateRandom(ulong seed)
    {
        var rng = new RandomNumberGenerator { Seed = seed };
        var list = new List<(Vector2, Vector2)>();

        int target   = rng.RandiRange(6, 10);
        int attempts = 0;

        while (list.Count < target && attempts < 300)
        {
            attempts++;

            var size   = new Vector2(rng.RandiRange(44, 130), rng.RandiRange(44, 130));
            var center = new Vector2(
                rng.RandfRange(160f, ArenaW - 160f),
                rng.RandfRange(150f, ArenaH - 150f));

            if (OverlapsSpawn(center, size) || OverlapsExisting(center, size, list))
                continue;

            list.Add((center, size));
        }

        // Guarantee at least a couple of obstacles even on an unlucky seed.
        if (list.Count == 0)
            return Classic;

        return list.ToArray();
    }

    // Reject placements that sit on top of a spawn point (keeps 80px of breathing room).
    private static bool OverlapsSpawn(Vector2 center, Vector2 size)
    {
        foreach (var s in SpawnKeepClear)
            if (Mathf.Abs(s.X - center.X) < size.X / 2f + 80f &&
                Mathf.Abs(s.Y - center.Y) < size.Y / 2f + 80f)
                return true;
        return false;
    }

    // Reject placements that touch another obstacle; require a 60px gap so enemies can pass.
    private static bool OverlapsExisting(Vector2 center, Vector2 size, List<(Vector2 C, Vector2 S)> list)
    {
        foreach (var (c, s) in list)
            if (Mathf.Abs(c.X - center.X) < (size.X + s.X) / 2f + 60f &&
                Mathf.Abs(c.Y - center.Y) < (size.Y + s.Y) / 2f + 60f)
                return true;
        return false;
    }
}
