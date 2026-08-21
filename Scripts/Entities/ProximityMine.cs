using Godot;

namespace NoBoxHead;

/// <summary>
/// Placed obstacle that arms itself a moment after being dropped, then detonates the instant an
/// enemy wanders into its trigger radius, dealing splash damage like GrenadeProjectile. Placement
/// itself follows BarrelWeapon's "find a free spot nearby" pattern; this script only owns the
/// arm/trigger/explode lifecycle once it exists in the world.
/// </summary>
public partial class ProximityMine : Area2D
{
    [Export] public float Damage          = 160f;
    [Export] public float KnockbackForce  = 300f;
    [Export] public float ExplosionRadius = 100f;
    [Export] public float TriggerRadius   = 42f;  // detection ring — bigger than the mine itself
    // Delay before the mine can trigger at all. Without this, a mine dropped with an enemy
    // already at the very edge of TriggerRadius (BarrelWeapon's placement check only excludes
    // overlap at the *footprint* used for placement, which is smaller than TriggerRadius) could
    // detonate the same frame it's placed, before the player who dropped it even sees it appear.
    [Export] public float ArmDelay        = 0.6f;

    private const float MinDamageFactor = 0.5f;

    /// <summary>
    /// Mirror of a mine another player placed. The placer's own mine resolves the damage; a
    /// cosmetic copy still arms, still visually detonates, but never calls TakeDamage — same
    /// contract as every other cosmetic projectile in this project.
    /// </summary>
    public bool Cosmetic;

    private bool       _armed;
    private bool       _exploded;
    private ColorRect? _visual;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask  = 4; // enemies only — a mine doesn't care about walls or other mines
        Monitoring     = false; // enabled once armed, see ArmAfterDelay
        Monitorable    = false;

        _visual = new ColorRect
        {
            Color    = new Color(0.5f, 0.1f, 0.1f),
            Size     = new Vector2(16, 16),
            Position = new Vector2(-8, -8)
        };
        AddChild(_visual);
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = TriggerRadius } });

        BodyEntered += _ => Explode();

        ArmAfterDelay();
    }

    private async void ArmAfterDelay()
    {
        await ToSignal(GetTree().CreateTimer(ArmDelay, false), SceneTreeTimer.SignalName.Timeout);
        if (!IsInstanceValid(this)) return;
        _armed     = true;
        Monitoring = true;
        // Brighten once live so the placer (and anyone else) can tell it's active.
        if (_visual != null) _visual.Color = new Color(0.85f, 0.15f, 0.1f);
    }

    private void Explode()
    {
        if (!_armed || _exploded || !IsInstanceValid(this)) return;
        _exploded = true;
        // Deferred: Explode() runs from BodyEntered, and Godot refuses to flip monitoring while
        // it is emitting that very signal ("Function blocked during in/out signal"). The
        // _exploded guard above already stops a second trigger, so waiting a frame to actually
        // switch it off costs nothing.
        SetDeferred(Area2D.PropertyName.Monitoring, false);
        AudioManager.Instance?.Play(AudioManager.Explosion);

        if (Cosmetic) { ShowBlast(); return; }

        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not (IDamageable and Node2D)) continue;
            var d   = (IDamageable)node;
            var n2d = (Node2D)node;
            if (!d.IsAlive) continue;

            float dist = GlobalPosition.DistanceTo(n2d.GlobalPosition);
            if (dist > ExplosionRadius) continue;

            float falloff = 1f - dist / ExplosionRadius;
            d.TakeDamage(Damage * Mathf.Max(falloff, MinDamageFactor));

            var away = dist > 1f ? (n2d.GlobalPosition - GlobalPosition).Normalized() : Vector2.Up;
            BloodSystem.Instance?.Splatter(n2d.GlobalPosition, away, 1.3f);

            if (node is IKnockbackable kb)
                kb.ApplyKnockback(away * KnockbackForce * Mathf.Max(falloff, 0.4f));
        }

        ShowBlast();
    }

    private async void ShowBlast()
    {
        var blast = new ColorRect
        {
            Color    = new Color(1f, 0.5f, 0.1f, 0.75f),
            Size     = new Vector2(ExplosionRadius * 2f, ExplosionRadius * 2f),
            ZIndex   = 5,
        };
        GetParent()?.AddChild(blast);
        // Centred here rather than through Position before AddChild — see GrenadeProjectile.
        blast.GlobalPosition = GlobalPosition - new Vector2(ExplosionRadius, ExplosionRadius);

        var tween = blast.CreateTween();
        tween.TweenProperty(blast, "modulate:a", 0f, 0.25f);
        tween.TweenCallback(Godot.Callable.From(blast.QueueFree));

        await ToSignal(GetTree().CreateTimer(0.02), SceneTreeTimer.SignalName.Timeout);
        if (IsInstanceValid(this)) QueueFree();
    }
}
