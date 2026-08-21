using Godot;

namespace NoBoxHead;

/// <summary>
/// Bolt fired by the Cryo Gun. Travels in a straight line like Bullet, but is its own Area2D
/// rather than reusing Bullet: every existing use of Bullet.OnImpact only ever needed the impact
/// POINT (fragments, tracers), never which body was actually struck, so the hook doesn't carry
/// one. This weapon needs the struck enemy itself, to call EnemyBase.ApplySlow on it.
/// </summary>
public partial class CryoProjectile : Area2D
{
    [Export] public float Speed          = 480f;
    [Export] public float Damage         = 8f;    // light — the payoff here is the slow, not raw damage
    [Export] public float KnockbackForce = 60f;
    [Export] public float MaxDistance    = 520f;
    [Export] public float SlowFactor     = 0.35f; // fraction of normal MoveSpeed retained while slowed
    [Export] public float SlowDuration   = 3f;

    /// <summary>
    /// Mirror of another player's shot, spawned purely so other peers can see it. The shooter's
    /// own bolt is the one that resolves damage and the slow — a cosmetic copy does neither,
    /// same contract as every other projectile here (RocketProjectile, ProximityMine, Bullet).
    /// </summary>
    public bool Cosmetic;

    private Vector2 _origin;
    private Vector2 _direction;

    public void Init(Vector2 origin, Vector2 direction)
    {
        _origin        = origin;
        _direction     = direction.Normalized();
        GlobalPosition = origin;
        Rotation       = _direction.Angle() + Mathf.Pi / 2f;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask  = 5; // walls (1) + enemies (4), same as Bullet

        AddChild(new ColorRect
        {
            Color    = new Color(0.55f, 0.85f, 1f),
            Size     = new Vector2(7, 11),
            Position = new Vector2(-3.5f, -5.5f)
        });
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 4f } });

        BodyEntered += OnBodyEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        GlobalPosition += _direction * Speed * (float)delta;
        if (GlobalPosition.DistanceTo(_origin) >= MaxDistance)
            QueueFree();
    }

    private void OnBodyEntered(Node body)
    {
        bool hitLiveEnemy = body is IDamageable dmg && dmg.IsAlive;

        if (hitLiveEnemy && !Cosmetic)
        {
            ((IDamageable)body).TakeDamage(Damage);
            if (body is EnemyBase enemy) enemy.ApplySlow(SlowFactor, SlowDuration);
            if (body is IKnockbackable kb) kb.ApplyKnockback(_direction * KnockbackForce);
        }
        if (hitLiveEnemy)
            BloodSystem.Instance?.Splatter(GlobalPosition, _direction, 0.4f);

        ShowFrostBurst();
        QueueFree();
    }

    private void ShowFrostBurst()
    {
        var burst = new ColorRect
        {
            Color    = new Color(0.6f, 0.85f, 1f, 0.6f),
            Size     = new Vector2(20, 20),
            Position = new Vector2(-10, -10),
            ZIndex   = 4,
        };
        var parent = GetParent();
        if (parent == null) return;
        parent.AddChild(burst);
        burst.GlobalPosition = GlobalPosition;

        var tween = burst.CreateTween();
        tween.TweenProperty(burst, "modulate:a", 0f, 0.2f);
        tween.TweenCallback(Godot.Callable.From(burst.QueueFree));
    }
}
