using Godot;

namespace NoBoxHead;

/// <summary>
/// Rocket fired by the Rocket Launcher. Travels in a straight line like GrenadeProjectile, but
/// faster and with no meaningful arc, and on top of the usual splash it deals a large bonus
/// hit of damage to whatever it physically struck (a direct hit should always feel stronger
/// than catching splash from someone else's rocket).
/// </summary>
public partial class RocketProjectile : Area2D
{
    [Export] public float Speed           = 620f;  // faster than the grenade's 320
    [Export] public float Damage          = 90f;   // splash damage, applies to everyone in range
    [Export] public float DirectHitBonus  = 70f;   // extra damage only for the thing it struck
    [Export] public float KnockbackForce  = 320f;
    [Export] public float ExplosionRadius = 95f;   // tighter than the grenade — this is a direct-fire
                                                    // weapon, not an area-denial lob
    [Export] public float FuseTime        = 2.5f;  // long fallback only — a flat, fast shot should
                                                    // always hit a wall or enemy well before this

    private const float MinDamageFactor = 0.5f;

    /// <summary>
    /// Mirror of another player's rocket. The shooter's own rocket resolves the damage, so a
    /// cosmetic copy only plays the flight and the blast — otherwise a two-player game would
    /// deal splash damage twice for the same shot.
    /// </summary>
    public bool Cosmetic;

    private Vector2 _direction;
    private bool    _exploded;
    private Node?   _directHitNode;

    public void Init(Vector2 origin, Vector2 direction)
    {
        GlobalPosition = origin;
        _direction     = direction.Normalized();
        Rotation       = _direction.Angle() + Mathf.Pi / 2f;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask  = 5; // walls (1) + enemies (4) — same as Bullet/Grenade

        AddChild(new ColorRect
        {
            Color    = new Color(0.6f, 0.15f, 0.1f),
            Size     = new Vector2(8, 16),
            Position = new Vector2(-4, -8)
        });
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 5f } });

        BodyEntered += body => Explode(body);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_exploded) return;

        GlobalPosition += _direction * Speed * (float)delta;

        FuseTime -= (float)delta;
        if (FuseTime <= 0f) Explode(null);
    }

    private void Explode(Node? directHit)
    {
        if (_exploded || !IsInstanceValid(this)) return;
        _exploded = true;
        _directHitNode = directHit;
        SetPhysicsProcess(false);
        AudioManager.Instance?.Play(AudioManager.Explosion);

        // Only the shooter's copy resolves damage — a mirrored rocket would double every hit.
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
            float dmg = Damage * Mathf.Max(falloff, MinDamageFactor);
            // Whatever the rocket actually struck takes the direct-hit bonus on top of splash,
            // ungated by falloff — a point-blank rocket should never feel weaker than a graze.
            if (ReferenceEquals(node, _directHitNode)) dmg += DirectHitBonus;
            d.TakeDamage(dmg);

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
            Color    = new Color(1f, 0.4f, 0.05f, 0.75f),
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
