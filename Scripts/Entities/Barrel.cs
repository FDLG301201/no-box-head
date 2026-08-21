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

    // Bodies this barrel spawned on top of, which it ignores until they step clear. See
    // SoftSpawn.
    private readonly System.Collections.Generic.List<PhysicsBody2D> _passThrough = new();

    private static readonly Color BarrelColor = new(0.55f, 0.35f, 0.1f);

    public override void _Ready()
    {
        _health = MaxHealth;
        CollisionLayer = 1; // same layer as walls/obstacles: blocks players and enemies alike
        CollisionMask  = 0;
        AddToGroup("barrels");
        BuildVisual();
        SoftSpawn();

        if (ArenaRef != null)
        {
            ArenaRef.RegisterBarrel(this);
            _registered = true;
        }
    }

    /// <summary>
    /// A barrel is dropped at the player's own feet, so at the instant it appears the player is
    /// standing inside a solid box. The physics engine can only resolve that one way — by
    /// pushing the player out — which is the sideways shove that made placement feel wrong.
    ///
    /// So the barrel starts intangible to whoever it landed on, and becomes solid to them the
    /// moment they step clear. Everyone else is blocked from the first frame, so a barrel wall
    /// still stops enemies and teammates immediately.
    ///
    /// The exception is registered on the OTHER body rather than on the barrel: the other body
    /// is the one running MoveAndSlide, and that is where the exception list is consulted.
    /// </summary>
    private void SoftSpawn()
    {
        foreach (var group in new[] { "players", "enemies" })
            foreach (var node in GetTree().GetNodesInGroup(group))
            {
                if (node is not PhysicsBody2D body || !IsInstanceValid(body)) continue;
                if (!Overlaps(body.GlobalPosition)) continue;
                body.AddCollisionExceptionWith(this);
                _passThrough.Add(body);
            }
        SetPhysicsProcess(_passThrough.Count > 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        for (int i = _passThrough.Count - 1; i >= 0; i--)
        {
            var body = _passThrough[i];
            // A body freed while still overlapping (an enemy killed on top of the barrel) takes
            // its exception with it; just drop the stale reference.
            if (!IsInstanceValid(body)) { _passThrough.RemoveAt(i); continue; }
            if (Overlaps(body.GlobalPosition)) continue;
            body.RemoveCollisionExceptionWith(this);
            _passThrough.RemoveAt(i);
        }
        // Nothing left to watch: stop burning a physics callback for every barrel on the map.
        if (_passThrough.Count == 0) SetPhysicsProcess(false);
    }

    public override void _ExitTree()
    {
        foreach (var body in _passThrough)
            if (IsInstanceValid(body)) body.RemoveCollisionExceptionWith(this);
        _passThrough.Clear();
    }

    // Circle-vs-box: closest point on the barrel to the body centre, compared against a radius
    // generous enough to cover the largest character (the ogre) rather than assuming the player.
    private const float BodyRadius = 26f;

    private bool Overlaps(Vector2 point)
    {
        var half = Size / 2f;
        var d = point - GlobalPosition;
        var closest = new Vector2(
            Mathf.Clamp(d.X, -half.X, half.X),
            Mathf.Clamp(d.Y, -half.Y, half.Y));
        return d.DistanceSquaredTo(closest) < BodyRadius * BodyRadius;
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
