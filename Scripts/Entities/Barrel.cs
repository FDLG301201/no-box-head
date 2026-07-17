using Godot;

namespace NoBoxHead;

/// <summary>
/// Destructible obstacle the player can place with the Barrel weapon to block a path.
/// Carved into the nav mesh like any other obstacle (via Arena.RegisterBarrel), so zombies
/// route around it when possible; if that leaves the player unreachable, Enemy/Demon target
/// the nearest barrel instead and beat it down until the path opens back up.
/// </summary>
public partial class Barrel : StaticBody2D, IDamageable
{
    [Export] public float MaxHealth = 30f;

    // Footprint used by Arena when carving this barrel into the navigation mesh.
    public Vector2 Size { get; } = new(30, 30);

    public Arena? ArenaRef { get; set; }

    public bool IsAlive => _health > 0f;

    private float      _health;
    private ColorRect? _visual;
    private bool       _registered;

    private static readonly Color BarrelColor = new(0.55f, 0.35f, 0.1f);

    public override void _Ready()
    {
        _health = MaxHealth;
        CollisionLayer = 1; // same layer as walls/obstacles: blocks players and enemies alike
        CollisionMask  = 0;
        AddToGroup("barrels");
        BuildVisual();

        if (ArenaRef != null)
        {
            ArenaRef.RegisterBarrel(this);
            _registered = true;
        }
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive) return;
        _health = Mathf.Max(0f, _health - amount);
        FlashDamage();
        if (_health <= 0f) Destroy();
    }

    private void Destroy()
    {
        SetPhysicsProcess(false);
        if (_registered)
        {
            ArenaRef?.UnregisterBarrel(this);
            _registered = false;
        }
        CallDeferred(Node.MethodName.QueueFree);
    }

    private async void FlashDamage()
    {
        if (_visual != null) _visual.Color = Colors.White;
        await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        if (IsInstanceValid(this) && _visual != null) _visual.Color = BarrelColor;
    }

    private void BuildVisual()
    {
        _visual = new ColorRect
        {
            Color    = BarrelColor,
            Size     = Size,
            Position = -Size / 2f
        };
        AddChild(_visual);
        AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = Size } });
    }
}
