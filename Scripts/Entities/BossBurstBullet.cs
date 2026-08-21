using Godot;

namespace NoBoxHead;

/// <summary>
/// One of the 8 bullets a BossShell bursts into. Straight-line travel, damages on first player
/// hit, same Cosmetic double-damage guard as DemonProjectile.
/// </summary>
public partial class BossBurstBullet : Area2D
{
    public float Damage   = 16f;
    public float Speed    = 260f;
    public float Lifetime = 2.2f;

    /// <summary>See DemonProjectile.Cosmetic.</summary>
    public bool Cosmetic;

    private Vector2 _direction;

    public void Init(Vector2 origin, Vector2 direction)
    {
        GlobalPosition = origin;
        _direction     = direction.Normalized();
        Rotation       = _direction.Angle() + Mathf.Pi / 2f;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask  = 3; // walls (1) + players (2)

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 5f } });
        AddChild(new ColorRect
        {
            Color    = new Color(0.85f, 0.15f, 0.85f),
            Size     = new Vector2(10, 10),
            Position = new Vector2(-5, -5)
        });

        BodyEntered += OnBodyEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        GlobalPosition += _direction * Speed * (float)delta;
        Lifetime -= (float)delta;
        if (Lifetime <= 0f) QueueFree();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (!Cosmetic && body is Player player)
            player.TakeDamage(Damage);
        QueueFree();
    }
}
