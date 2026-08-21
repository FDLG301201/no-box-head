using Godot;
using NoBoxHead;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Behavioural characterization harness for every enemy type. Prints one machine-comparable
/// line per measurement; the output is DETERMINISTIC (verified byte-identical across runs), so
/// it works as a regression oracle: capture it before a change, compare after, and any drift is
/// a real behavioural difference rather than noise.
///
/// It measures what a player would notice — stats, how far each type closes in five seconds,
/// health after a fixed hit, death, score, and group cleanup — rather than internals, so it
/// keeps working across refactors. It caught nothing during the EnemyBase extraction, which was
/// exactly the point: it proved 1175 lines could collapse to 720 without changing the game.
///
/// Run:
///   dotnet build
///   Godot_v4.7-stable_mono_win64_console.exe --path . --headless res://Tests/EnemyBaseline.tscn
///
/// Compare the `[BASE]` lines before and after your change. They must match exactly, decimals
/// included. Never "fix" a mismatch by editing this file or a tuning constant.
/// </summary>
public partial class EnemyBaseline : Node2D
{
    private const string PlayerScene = "res://Scenes/Entities/Player.tscn";

    private static readonly (string Name, string Scene)[] Kinds =
    {
        ("Enemy",    "res://Scenes/Entities/Enemy.tscn"),
        ("Sprinter", "res://Scenes/Entities/Sprinter.tscn"),
        ("Ogre",     "res://Scenes/Entities/Ogre.tscn"),
        ("Demon",    "res://Scenes/Entities/Demon.tscn"),
    };

    private Player                _player = null!;
    private readonly List<Node2D> _spawned = new();
    private readonly List<float>  _startDist = new();
    private int _frame;

    public override void _Ready()
    {
        var nav = new NavigationRegion2D();
        var poly = new NavigationPolygon();
        poly.AddOutline(new[]
        {
            new Vector2(36, 36), new Vector2(1244, 36),
            new Vector2(1244, 684), new Vector2(36, 684),
        });
        poly.MakePolygonsFromOutlines();
        nav.NavigationPolygon = poly;
        AddChild(nav);

        _player = ResourceLoader.Load<PackedScene>(PlayerScene).Instantiate<Player>();
        _player.Name = "Player0";
        AddChild(_player);
        _player.GlobalPosition = new Vector2(400, 400);

        // Each enemy gets its own lane far from the others so crowd separation between
        // different types never perturbs the measurement.
        for (int i = 0; i < Kinds.Length; i++)
        {
            var e = ResourceLoader.Load<PackedScene>(Kinds[i].Scene).Instantiate<Node2D>();
            e.Name = $"K{i}";
            AddChild(e);
            e.GlobalPosition = new Vector2(700 + i * 120, 200 + i * 110);
            if (e is Demon d) d.SetProjectileContainer(this);
            _spawned.Add(e);
            _startDist.Add(e.GlobalPosition.DistanceTo(_player.GlobalPosition));
        }

        GD.Print("[BASE] stats: name maxHealth moveSpeed attackDamage attackRange");
        for (int i = 0; i < _spawned.Count; i++)
            GD.Print($"[BASE] STAT {Kinds[i].Name} " +
                     $"hp={_spawned[i].Get("MaxHealth").AsSingle():F0} " +
                     $"spd={_spawned[i].Get("MoveSpeed").AsSingle():F0} " +
                     $"dmg={_spawned[i].Get("AttackDamage").AsSingle():F0} " +
                     $"rng={_spawned[i].Get("AttackRange").AsSingle():F0}");
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;

        // 5s of free chase: measures navigation, speed and stopping distance together.
        if (_frame == 300)
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                float moved = _startDist[i] - _spawned[i].GlobalPosition.DistanceTo(_player.GlobalPosition);
                GD.Print($"[BASE] CHASE {Kinds[i].Name} closed={moved:F1}px " +
                         $"dist={_spawned[i].GlobalPosition.DistanceTo(_player.GlobalPosition):F1}");
            }
            GD.Print($"[BASE] PLAYER hp={_player.CurrentHealth:F0} (damage taken while they closed in)");
        }
        // Damage model: a fixed 10 hp hit, then a lethal one. Checks health maths and death.
        else if (_frame == 320)
        {
            foreach (var (e, i) in _spawned.Select((e, i) => (e, i)))
            {
                ((IDamageable)e).TakeDamage(10f);
                GD.Print($"[BASE] DMG10 {Kinds[i].Name} hp={e.Get("_currentHealth").AsSingle():F1}");
            }
        }
        else if (_frame == 340)
        {
            int scoreBefore = ScoreManager.Instance?.Score ?? -1;
            foreach (var e in _spawned)
                ((IDamageable)e).TakeDamage(e.Get("MaxHealth").AsSingle());
            GD.Print($"[BASE] scoreBefore={scoreBefore}");
        }
        else if (_frame == 400)
        {
            for (int i = 0; i < _spawned.Count; i++)
                GD.Print($"[BASE] DEATH {Kinds[i].Name} alive={((IDamageable)_spawned[i]).IsAlive} " +
                         $"stillInTree={IsInstanceValid(_spawned[i]) && _spawned[i].IsInsideTree()}");
            GD.Print($"[BASE] scoreAfter={ScoreManager.Instance?.Score ?? -1}");
            GD.Print($"[BASE] enemiesGroup={GetTree().GetNodesInGroup("enemies").Count}");
            GetTree().Quit();
        }
    }
}
