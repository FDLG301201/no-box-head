using Godot;

namespace NoBoxHead;

/// <summary>
/// Thrown grenade. Travels in a straight line and detonates on hitting a wall/enemy or
/// after its fuse runs out, dealing splash damage and knockback to every enemy in range.
/// </summary>
public partial class GrenadeProjectile : Area2D
{
    [Export] public float Speed            = 320f;
    [Export] public float Damage           = 90f;
    [Export] public float KnockbackForce   = 260f;
    [Export] public float ExplosionRadius  = 95f;
    [Export] public float FuseTime         = 1.1f;

    private Vector2 _direction;
    private bool    _exploded;

    public void Init(Vector2 origin, Vector2 direction)
    {
        GlobalPosition = origin;
        _direction     = direction.Normalized();
        Rotation       = _direction.Angle() + Mathf.Pi / 2f;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask  = 5; // walls (1) + enemies (4) — same as Bullet

        AddChild(new ColorRect
        {
            Color    = new Color(0.2f, 0.5f, 0.15f),
            Size     = new Vector2(10, 10),
            Position = new Vector2(-5, -5)
        });
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 5f } });

        BodyEntered += _ => Explode();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_exploded) return;

        GlobalPosition += _direction * Speed * (float)delta;

        FuseTime -= (float)delta;
        if (FuseTime <= 0f) Explode();
    }

    private void Explode()
    {
        if (_exploded || !IsInstanceValid(this)) return;
        _exploded = true;
        SetPhysicsProcess(false);

        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not (IDamageable and Node2D)) continue;
            var d   = (IDamageable)node;
            var n2d = (Node2D)node;
            if (!d.IsAlive) continue;

            float dist = GlobalPosition.DistanceTo(n2d.GlobalPosition);
            if (dist > ExplosionRadius) continue;

            float falloff = 1f - dist / ExplosionRadius;
            d.TakeDamage(Damage * Mathf.Max(falloff, 0.35f));

            if (node is IKnockbackable kb)
            {
                var away = dist > 1f ? (n2d.GlobalPosition - GlobalPosition).Normalized() : Vector2.Up;
                kb.ApplyKnockback(away * KnockbackForce * Mathf.Max(falloff, 0.4f));
            }
        }

        ShowBlast();
    }

    private async void ShowBlast()
    {
        var blast = new ColorRect
        {
            Color    = new Color(1f, 0.55f, 0.1f, 0.75f),
            Size     = new Vector2(ExplosionRadius * 2f, ExplosionRadius * 2f),
            Position = -new Vector2(ExplosionRadius, ExplosionRadius),
            ZIndex   = 5,
        };
        GetParent()?.AddChild(blast);
        blast.GlobalPosition = GlobalPosition;

        var tween = blast.CreateTween();
        tween.TweenProperty(blast, "modulate:a", 0f, 0.25f);
        tween.TweenCallback(Godot.Callable.From(blast.QueueFree));

        await ToSignal(GetTree().CreateTimer(0.02), SceneTreeTimer.SignalName.Timeout);
        if (IsInstanceValid(this)) QueueFree();
    }
}
