using Godot;

namespace NoBoxHead;

/// <summary>
/// Slow fireball fired by the Demon enemy toward the nearest player.
/// Destroyed on hitting a wall or player.
/// </summary>
public partial class DemonProjectile : Area2D
{
    public float Damage = 20f;
    public float Speed  = 200f;

    private Vector2 _direction;
    private float   _lifetime = 5f;

    public void Init(Vector2 origin, Vector2 direction, float damage = 20f)
    {
        GlobalPosition = origin;
        _direction     = direction.Normalized();
        Damage         = damage;
        Rotation       = _direction.Angle() + Mathf.Pi / 2f;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask  = 3; // walls (1) + players (2)

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 6f } });
        AddChild(new ColorRect
        {
            Color    = new Color(1f, 0.35f, 0f),
            Size     = new Vector2(12, 12),
            Position = new Vector2(-6, -6)
        });

        BodyEntered += OnBodyEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        GlobalPosition += _direction * Speed * (float)delta;
        _lifetime -= (float)delta;
        if (_lifetime <= 0f) QueueFree();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is Player player)
            player.TakeDamage(Damage);
        QueueFree();
    }
}
