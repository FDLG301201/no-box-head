using Godot;

namespace NoBoxHead;

/// <summary>
/// Fast-moving projectile fired by the player.
/// Destroyed on hitting a wall (layer 1) or any IDamageable enemy (layer 4).
/// </summary>
public partial class Bullet : Area2D
{
    [Export] public float Speed          = 500f;
    [Export] public float Damage         = 15f;
    [Export] public float MaxDistance    = 600f;
    // 1f = no falloff; 0.15f = 15% damage at max range (used by shotgun pellets).
    public float MinDamageFactor  = 1f;
    // Falloff begins this fraction into the range. E.g. 0.3 → full damage for first 30%.
    public float FalloffStartRatio = 0.3f;
    // Force applied to the hit enemy's velocity vector.
    public float KnockbackForce   = 100f;

    private Vector2 _direction;
    private Vector2 _origin;

    public void Init(Vector2 origin, Vector2 direction, float damage)
    {
        _origin        = origin;
        _direction     = direction.Normalized();
        Damage         = damage;
        GlobalPosition = origin;
        Rotation       = direction.Angle() + Mathf.Pi / 2f;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask  = 5; // walls (1) + enemies (4)

        BodyEntered += OnBodyEntered;

        AddChild(new ColorRect
        {
            Color    = new Color(1f, 0.9f, 0.2f),
            Size     = new Vector2(6, 10),
            Position = new Vector2(-3, -5)
        });
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 4f } });
    }

    public override void _PhysicsProcess(double delta)
    {
        GlobalPosition += _direction * Speed * (float)delta;
        if (GlobalPosition.DistanceTo(_origin) >= MaxDistance)
            QueueFree();
    }

    private void OnBodyEntered(Node body)
    {
        if (body is IDamageable damageable && damageable.IsAlive)
            damageable.TakeDamage(ComputeDamage());
        if (body is IKnockbackable kb)
            kb.ApplyKnockback(_direction * KnockbackForce);
        QueueFree();
    }

    private float ComputeDamage()
    {
        if (MinDamageFactor >= 1f || MaxDistance <= 0f) return Damage;
        float dist        = GlobalPosition.DistanceTo(_origin);
        float fallStart   = MaxDistance * FalloffStartRatio;
        float fallRange   = MaxDistance - fallStart;
        float t           = Mathf.Clamp((dist - fallStart) / fallRange, 0f, 1f);
        return Damage * Mathf.Lerp(1f, MinDamageFactor, t);
    }
}
