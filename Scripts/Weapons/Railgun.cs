using Godot;

namespace NoBoxHead;

/// <summary>
/// Slow, single-shot rail weapon. The bolt punches through several enemies in a straight line
/// instead of stopping at the first one, at the cost of a long fire-rate cooldown and a small
/// magazine. Reuses the plain Bullet scene — piercing is Bullet.PierceCount, added specifically
/// for this weapon.
/// </summary>
public partial class Railgun : Weapon
{
    // Bullet stops after damaging this many enemies beyond the first (i.e. up to 5 total hits).
    private const int   PierceThroughCount = 4;
    private const float RailRange          = 900f; // longer than every hitscan-ish weapon here

    public override string WeaponName => "Railgun";
    protected override string FireSound => AudioManager.Railgun;
    // Blue-violet energy bolt — visually distinct from every other (yellow/warm) projectile,
    // matching the "electromagnetic rail" theme instead of a fired cartridge.
    protected override Color BulletColor => new Color(0.55f, 0.25f, 0.95f);

    public override void _Ready()
    {
        FireRate         = 1.6f;   // slow — the payoff is piercing damage, not rate of fire
        MagazineSize     = 3;
        ReloadTime       = 2.6f;
        BulletDamage     = 70f;    // high per-hit damage, multiplied across pierced enemies
        StartReserveAmmo = 6;
        MaxReserveAmmo   = 18;
        BulletKnockback  = 260f;
        base._Ready();
    }

    protected override void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        if (BulletScene == null) return;
        var bolt = BulletScene.Instantiate<Bullet>();
        bolt.Damage         = BulletDamage;
        bolt.KnockbackForce = BulletKnockback;
        bolt.MaxDistance    = RailRange;
        bolt.PierceCount    = PierceThroughCount;
        bolt.Color          = BulletColor;
        (BulletContainer ?? GetTree().Root).AddChild(bolt);
        bolt.Init(origin, direction, BulletDamage);

        BroadcastTracer(origin, direction, RailRange, bolt.Speed, PierceThroughCount);
    }
}
