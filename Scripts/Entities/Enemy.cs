using Godot;

namespace NoBoxHead;

/// <summary>
/// Zombie enemy. Navigates around walls using NavigationAgent2D.
/// Simulated on host; state replicated via RPC.
/// The baseline enemy type — see EnemyBase for the shared chase/knockback/damage/death
/// machinery every enemy type uses.
/// </summary>
public partial class Enemy : EnemyBase
{
    public Enemy()
    {
        MoveSpeed      = 30f;
        MaxHealth      = 30f;
        AttackDamage   = 10f;
        AttackCooldown = 1.0f;
        // Must be > sum of radii (player=12, enemy=11 = 23) so attack fires while touching.
        AttackRange    = 30f;
    }

    protected override void BuildVisual()
    {
        // Canvas 506x453, character's visual centre (258, 226.5) — taken from the art's ALPHA BOUNDS, not the
        // canvas middle, so the body stays pinned to the node origin where the collision circle
        // and pathing live. Both numbers are derived, never eyeballed: after any art re-export run
        // `python Tools/sprite_metrics.py emit` and paste what it prints.
        const float scale = 0.10000f;
        _visual = new Sprite2D
        {
            Texture  = ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Enemies/zombie.png"),
            Centered = false,
            Scale    = new Vector2(scale, scale),
            Position = new Vector2(-258f * scale, -226.5f * scale),
        };
        AddChild(_visual);

        AddChild(new ColorRect
        {
            Color    = new Color(0.2f, 0.2f, 0.2f),
            Size     = new Vector2(30, 4),
            Position = new Vector2(-15, -29)
        });

        _healthFill = new ColorRect
        {
            Color    = new Color(0.9f, 0.2f, 0.2f),
            Size     = new Vector2(30, 4),
            Position = new Vector2(-15, -29)
        };
        AddChild(_healthFill);
        _healthBarWidth  = 30f;
        _healthBarHeight = 4f;

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 11f } });
    }
}
