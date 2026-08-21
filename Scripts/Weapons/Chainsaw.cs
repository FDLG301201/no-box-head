using Godot;

namespace NoBoxHead;

/// <summary>
/// Melee upgrade of Knife: same "swing while in the facing cone" resolution, but a much faster
/// tick rate gives continuous damage while the fire button is held, at shorter range and lower
/// per-tick damage than a knife swing. Infinite uses, same as Knife.
/// </summary>
public partial class Chainsaw : Weapon
{
    private const float TickDamage      = 4f;
    // Measured the same way as Knife.MeleeRange, but shorter — a chainsaw is a close, sustained
    // grind, not a lunging stab.
    private const float MeleeRange      = 55f;
    private const float KnockbackImpulse = 40f; // light, continuous — no single big shove
    private const float FacingThreshold = 0.3f;

    public override string WeaponName => "Chainsaw";

    // Set by Player to trigger a swing/idle visual on the player node, same hook as Knife.
    public System.Action<Vector2>? OnAttack { get; set; }

    public override void _Ready()
    {
        FireRate         = 0.1f;  // fast tick → reads as continuous while held
        MagazineSize     = -1;    // sentinel for ∞, same as Knife
        StartReserveAmmo = -1;
        MaxReserveAmmo   = -1;
        BulletDamage     = TickDamage;
        base._Ready();
    }

    // Override fully — no ammo logic, same as Knife.
    public override void TryShoot(Vector2 origin, Vector2 direction)
    {
        if (_fireCooldown > 0f) return;
        _fireCooldown = FireRate;
        AudioManager.Instance?.Play(AudioManager.Knife, 0.6f, 0.15f);
        PerformMeleeAttack(origin, direction);
    }

    private void PerformMeleeAttack(Vector2 origin, Vector2 direction)
    {
        OnAttack?.Invoke(direction);

        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not (IDamageable and Node2D)) continue;
            var d   = (IDamageable)node;
            var n2d = (Node2D)node;
            if (!d.IsAlive) continue;

            var toEnemy = n2d.GlobalPosition - origin;
            if (toEnemy.Length() > MeleeRange) continue;
            if (direction.Dot(toEnemy.Normalized()) < FacingThreshold) continue;

            d.TakeDamage(TickDamage);
            BloodSystem.Instance?.Splatter(n2d.GlobalPosition, direction, 0.5f);
            if (node is IKnockbackable kb)
                kb.ApplyKnockback(direction * KnockbackImpulse);
        }
    }
}
