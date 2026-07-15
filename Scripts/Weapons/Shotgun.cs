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
            (BulletContainer ?? GetTree().Root).AddChild(pellet);
            pellet.Init(origin, dir, BulletDamage);
        }
    }
}
