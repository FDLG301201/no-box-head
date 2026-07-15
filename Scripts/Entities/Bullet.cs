using Godot;

namespace NoBoxHead;

/// <summary>
/// Fast-moving projectile that deals damage on hitting an Enemy.
/// Destroyed after a fixed travel distance or on collision.
/// </summary>
public partial class Bullet : Area2D
{
    [Export] public float Speed = 500f;
    [Export] public float Damage = 15f;
    [Export] public float MaxDistance = 600f;

    private Vector2 _direction;
    private Vector2 _origin;

    public void Init(Vector2 origin, Vector2 direction, float damage)
    {
        _origin = origin;
        _direction = direction.Normalized();
        Damage = damage;
        GlobalPosition = origin;
        Rotation = direction.Angle() + Mathf.Pi / 2f;
    }

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;

        // Placeholder visual: small yellow rect.
        var rect = new ColorRect
        {
            Color = new Color(1f, 0.9f, 0.2f),
            Size = new Vector2(6, 10),
            Position = new Vector2(-3, -5)
        };
        AddChild(rect);

        var shape = new CollisionShape2D();
        shape.Shape = new CircleShape2D { Radius = 4f };
        AddChild(shape);

        CollisionLayer = 0;
        CollisionMask = 4; // layer 3 = enemies
    }

    public override void _PhysicsProcess(double delta)
    {
        GlobalPosition += _direction * Speed * (float)delta;

        if (GlobalPosition.DistanceTo(_origin) >= MaxDistance)
            QueueFree();
    }

    private void OnBodyEntered(Node body)
    {
        if (body is Enemy enemy && enemy.IsAlive)
        {
            enemy.TakeDamage(Damage);
            QueueFree();
        }
    }
}
