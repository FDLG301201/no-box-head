using Godot;

namespace NoBoxHead;

/// <summary>
/// Food pickup that restores health to whoever walks over it. Drawn as a chunky stacked
/// burger to match the game's blocky art style — swap BuildVisual for a Sprite2D if a
/// dedicated food sprite is added later.
/// </summary>
public partial class HealthPack : Area2D
{
    [Export] public float HealAmount = 15f;

    private bool _pickedUp;

    // Burger layers, drawn bottom-up: bun, patty, lettuce, bun top.
    private static readonly Color BunColor     = new(0.85f, 0.62f, 0.28f);
    private static readonly Color PattyColor   = new(0.42f, 0.24f, 0.12f);
    private static readonly Color LettuceColor = new(0.35f, 0.72f, 0.28f);

    public override void _Ready()
    {
        CollisionLayer = 8;
        CollisionMask  = 2; // players only
        Monitoring     = true;
        Monitorable    = true;

        BuildVisual();
        BodyEntered += OnBodyEntered;
        Bob();
    }

    private void OnBodyEntered(Node2D body)
    {
        // Guard so two co-op players can't both consume the same pack.
        if (_pickedUp || body is not Player player) return;
        if (!player.Heal(HealAmount)) return; // already at full health — leave it on the ground
        _pickedUp = true;
        AudioManager.Instance?.Play(AudioManager.PickupHealth, 0.8f);
        QueueFree();
    }

    private void BuildVisual()
    {
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 14f } });

        AddLayer(BunColor,     new Vector2(20, 5), new Vector2(-10,  3));  // bottom bun
        AddLayer(PattyColor,   new Vector2(22, 5), new Vector2(-11, -2));  // patty
        AddLayer(LettuceColor, new Vector2(22, 3), new Vector2(-11, -5));  // lettuce
        AddLayer(BunColor,     new Vector2(20, 7), new Vector2(-10, -11)); // top bun
    }

    private void AddLayer(Color color, Vector2 size, Vector2 pos) =>
        AddChild(new ColorRect { Color = color, Size = size, Position = pos });

    // Gentle hover so pickups read as collectible rather than scenery.
    private void Bob()
    {
        var tween = CreateTween().SetLoops();
        tween.TweenProperty(this, "position:y", Position.Y - 3f, 0.7)
             .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(this, "position:y", Position.Y + 3f, 0.7)
             .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>Rolls a drop chance and spawns a pack at the given spot.</summary>
    public static void TryDrop(Node? parent, Vector2 position, float chance)
    {
        if (parent == null || GD.Randf() >= chance) return;
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/HealthPack.tscn");
        if (scene == null) return;
        var pack = scene.Instantiate<HealthPack>();
        pack.GlobalPosition = position;
        parent.AddChild(pack);
    }
}
