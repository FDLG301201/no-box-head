using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

/// <summary>
/// Fast-moving projectile fired by the player.
/// Destroyed on hitting a wall (layer 1) or any IDamageable enemy (layer 4), unless
/// PierceCount lets it punch through one or more enemies first (used by the Railgun).
/// </summary>
public partial class Bullet : Area2D
{
    [Export] public float Speed          = 500f;
    [Export] public float Damage         = 15f;
    [Export] public float MaxDistance    = 600f;
    // Projectile tint, set by the spawning weapon before it enters the tree (see Weapon.SpawnBullet
    // and the weapon overrides of Weapon.BulletColor). Defaults to the original yellow so any
    // weapon that never opts in renders exactly as it always has.
    [Export] public Color Color          = new Color(1f, 0.9f, 0.2f);
    // 1f = no falloff; 0.15f = 15% damage at max range (used by shotgun pellets).
    public float MinDamageFactor  = 1f;
    // Falloff begins this fraction into the range. E.g. 0.3 → full damage for first 30%.
    public float FalloffStartRatio = 0.3f;
    // Force applied to the hit enemy's velocity vector.
    public float KnockbackForce   = 100f;
    // Extra enemies this bullet can pass through after its first hit (Railgun). 0 = stops dead,
    // matching every other bullet's original behaviour.
    public int PierceCount;
    // Fired on the hit that finally stops the bullet (wall or pierce budget exhausted), so a
    // weapon can spawn something extra on impact (e.g. flak shotgun fragments) without a bespoke
    // projectile scene. Runs for cosmetic bullets too, so mirrored shots produce mirrored effects
    // without needing their own RPC.
    public System.Action<Vector2, Vector2>? OnImpact;

    /// <summary>
    /// A copy of somebody else's shot, spawned purely so the other players can see it. The
    /// shooter's own bullet is the one that resolves the hit (via the host), so a cosmetic
    /// round deals no damage and applies no knockback — otherwise every peer would resolve
    /// the same shot and a two-player game would do double damage.
    /// </summary>
    public bool Cosmetic;

    private Vector2 _direction;
    private Vector2 _origin;
    private int _piercesLeft;
    private readonly HashSet<ulong> _hitInstanceIds = new();

    public void Init(Vector2 origin, Vector2 direction, float damage)
    {
        _origin        = origin;
        _direction     = direction.Normalized();
        Damage         = damage;
        GlobalPosition = origin;
        Rotation       = direction.Angle() + Mathf.Pi / 2f;
        _piercesLeft   = PierceCount;
        _hitInstanceIds.Clear();
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask  = 5; // walls (1) + enemies (4)

        BodyEntered += OnBodyEntered;

        AddChild(new ColorRect
        {
            Color    = Color,
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
        bool hitLiveEnemy = body is IDamageable dmg && dmg.IsAlive;
        // Safety net: an Area2D shouldn't re-fire BodyEntered for a body it's still overlapping,
        // but guard against it anyway so a pierced bullet can never double-hit the same enemy.
        if (hitLiveEnemy && !_hitInstanceIds.Add(body.GetInstanceId()))
            return;

        if (hitLiveEnemy)
        {
            if (!Cosmetic) ((IDamageable)body).TakeDamage(ComputeDamage());
            // Blood is local decoration either way, so a mirrored shot still draws its hit.
            BloodSystem.Instance?.Splatter(GlobalPosition, _direction);
        }
        if (!Cosmetic && body is IKnockbackable kb)
            kb.ApplyKnockback(_direction * KnockbackForce);

        // Railgun: keep flying through enemies until the pierce budget runs out. Walls (not
        // IDamageable) always stop the bullet outright.
        if (hitLiveEnemy && _piercesLeft > 0)
        {
            _piercesLeft--;
            return;
        }

        OnImpact?.Invoke(GlobalPosition, _direction);
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
