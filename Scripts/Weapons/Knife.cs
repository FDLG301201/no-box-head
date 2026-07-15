using Godot;

namespace NoBoxHead;

/// <summary>
/// Melee knife. Always available; usable via V key or auto-equipped when ranged ammo runs out.
/// Infinite uses. Damage scales by +2.5 every 10 waves, capped below shotgun-pellet damage.
/// </summary>
public partial class Knife : Weapon
{
    private const float BaseDamage      = 7.5f;
    private const float DamagePerTen    = 2.5f;
    private const float MaxDamage       = 22.5f; // < shotgun pellet (25)
    private const float MeleeRange      = 55f;
    private const float KnockbackImpulse = 80f;
    // Dot-product threshold: only hits enemies roughly in front (>30° cone either side).
    private const float FacingThreshold = 0.3f;

    public override string WeaponName => "Knife";

    // Set by Player to trigger a swing visual on the player node.
    public System.Action<Vector2>? OnAttack { get; set; }

    public override void _Ready()
    {
        FireRate         = 0.35f;
        MagazineSize     = -1;   // sentinel for ∞
        StartReserveAmmo = -1;
        MaxReserveAmmo   = -1;
        BulletDamage     = BaseDamage;
        base._Ready();
    }

    // Override fully — knife has no ammo logic.
    public override void TryShoot(Vector2 origin, Vector2 direction)
    {
        if (_fireCooldown > 0f) return;
        _fireCooldown = FireRate;
        PerformMeleeAttack(origin, direction);
    }

    private void PerformMeleeAttack(Vector2 origin, Vector2 direction)
    {
        // Always trigger swing animation, regardless of hit.
        OnAttack?.Invoke(direction);

        float damage = ComputeDamage();
        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not (IDamageable and Node2D)) continue;
            var d   = (IDamageable)node;
            var n2d = (Node2D)node;
            if (!d.IsAlive) continue;

            var toEnemy = n2d.GlobalPosition - origin;
            if (toEnemy.Length() > MeleeRange) continue;
            if (direction.Dot(toEnemy.Normalized()) < FacingThreshold) continue;

            d.TakeDamage(damage);
            if (node is IKnockbackable kb)
                kb.ApplyKnockback(direction * KnockbackImpulse);
        }
    }

    private static float ComputeDamage()
    {
        int wave = GameManager.Instance?.CurrentWave ?? 1;
        return Mathf.Min(BaseDamage + (wave / 10) * DamagePerTen, MaxDamage);
    }

}
