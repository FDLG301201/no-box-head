using Godot;

namespace NoBoxHead;

/// <summary>
/// Continuous cone weapon: ticks damage every frame the fire button is held against whatever
/// stands in a short cone in front of the player, the same "in the facing cone" resolution as
/// Chainsaw's melee swing (see Chainsaw.PerformMeleeAttack) but ranged instead of a fixed lunge.
/// Damage is a local computation resolved through IDamageable.TakeDamage — exactly like the
/// Chainsaw and Knife — which already forwards a client's hit to the host on its own, so no RPC
/// is needed to stay multiplayer-safe. The visual flame cone is likewise local-only, matching
/// Chainsaw's swing effect (never mirrored to other peers either).
/// </summary>
public partial class Flamethrower : Weapon
{
    public override string WeaponName => "Flamethrower";
    protected override string FireSound => AudioManager.Flamethrower;
    // No BulletColor override here: this weapon never spawns a Bullet at all (damage is a local
    // cone check, see SpawnBullet below) — its projectile colour is the flame cone's own Polygon2D
    // Color in ShowFlameCone, which is already orange.

    private const float TickDamage      = 5f;
    private const float Range           = 150f;
    private const float HalfAngleRad    = 0.38f; // ~22° half-angle → ~44° total cone
    private const float KnockbackImpulse = 55f;

    public override void _Ready()
    {
        FireRate         = 0.09f; // fast tick → reads as a continuous stream while held
        MagazineSize     = 80;    // "fuel tank" — drains one unit per tick, same accounting as any weapon
        ReloadTime       = 2.4f;
        BulletDamage     = TickDamage;
        StartReserveAmmo = 120;
        MaxReserveAmmo   = 320;
        BulletKnockback  = KnockbackImpulse;
        base._Ready();
    }

    protected override void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        var dir = direction.Normalized();
        float cosHalfAngle = Mathf.Cos(HalfAngleRad);

        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not (IDamageable and Node2D)) continue;
            var d   = (IDamageable)node;
            var n2d = (Node2D)node;
            if (!d.IsAlive) continue;

            var toEnemy = n2d.GlobalPosition - origin;
            float dist  = toEnemy.Length();
            if (dist > Range) continue;
            // Guard the degenerate case (enemy exactly on the origin) so Normalized() never NaNs.
            if (dist > 1f && dir.Dot(toEnemy.Normalized()) < cosHalfAngle) continue;

            d.TakeDamage(TickDamage);
            BloodSystem.Instance?.Splatter(n2d.GlobalPosition, dir, 0.4f);
            if (node is IKnockbackable kb)
                kb.ApplyKnockback(dir * KnockbackImpulse);
        }

        ShowFlameCone(origin, dir);
    }

    private void ShowFlameCone(Vector2 origin, Vector2 dir)
    {
        if (GetTree().Root == null) return;

        var pts = new Vector2[]
        {
            Vector2.Zero,
            Vector2.FromAngle(dir.Angle() - HalfAngleRad) * Range,
            Vector2.FromAngle(dir.Angle()) * Range,
            Vector2.FromAngle(dir.Angle() + HalfAngleRad) * Range,
        };

        var flame = new Polygon2D
        {
            Polygon        = pts,
            Color          = new Color(1f, 0.5f, 0.1f, 0.55f),
            GlobalPosition = origin,
            ZIndex         = 5,
        };
        (BulletContainer ?? GetTree().Root).AddChild(flame);

        var tween = flame.CreateTween();
        tween.TweenProperty(flame, "modulate:a", 0f, 0.1f);
        tween.TweenCallback(Godot.Callable.From(flame.QueueFree));
    }
}
