using Godot;

namespace NoBoxHead;

/// <summary>
/// Pump-action shotgun. Fires 5 pellets in a 30° spread.
/// Full damage up to 30% of range, then falls to 15% at max range.
/// </summary>
public partial class Shotgun : Weapon
{
    private const int   PelletCount      = 5;
    private const float SpreadDegrees    = 30f;  // total cone (±15°)
    private const float PelletMaxRange   = 260f; // much shorter than pistol (600)
    private const float PelletMinDamage  = 0.15f;
    private const float PelletFalloff    = 0.3f; // falloff starts at 30% of range

    public override string WeaponName => "Shotgun";
    protected override string FireSound => AudioManager.Shotgun;
    // Warm amber pellets — close to the pistol's yellow (same "conventional ammo" family) but
    // visibly more orange, so a spread of pellets doesn't read as identical to a pistol round.
    protected override Color BulletColor => new Color(1f, 0.75f, 0.25f);

    public override void _Ready()
    {
        FireRate         = 0.9f;
        MagazineSize     = 8;
        ReloadTime       = 2.2f;
        BulletDamage     = 25f;    // per pellet; up to 125 total at point-blank
        StartReserveAmmo = 16;     // 2 extra magazines
        MaxReserveAmmo   = 64;
        base._Ready();
    }

    protected override void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        if (BulletScene == null) return;
        float baseAngle = direction.Angle();

        for (int i = 0; i < PelletCount; i++)
        {
            // Evenly distribute across the spread cone.
            float t      = (float)i / (PelletCount - 1) - 0.5f; // -0.5 .. +0.5
            float offset = t * Mathf.DegToRad(SpreadDegrees);
            var   dir    = Vector2.FromAngle(baseAngle + offset);

            var pellet = BulletScene.Instantiate<Bullet>();
            pellet.Damage            = BulletDamage;
            pellet.MaxDistance       = PelletMaxRange;
            pellet.MinDamageFactor   = PelletMinDamage;
            pellet.FalloffStartRatio = PelletFalloff;
            pellet.KnockbackForce    = 45f;
            pellet.Color             = BulletColor;
            (BulletContainer ?? GetTree().Root).AddChild(pellet);
            pellet.Init(origin, dir, BulletDamage);

            // One tracer per pellet, so the spread cone reads the same for everyone.
            BroadcastTracer(origin, dir, PelletMaxRange, pellet.Speed);
        }
    }
}
