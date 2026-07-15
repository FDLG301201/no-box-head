using Godot;

namespace NoBoxHead;

/// <summary>
/// Full-auto machine gun. 60-round magazine, double pistol damage, very high fire rate.
/// </summary>
public partial class MachineGun : Weapon
{
    public override string WeaponName => "Machine Gun";

    public override void _Ready()
    {
        FireRate         = 0.07f;  // ~857 RPM
        MagazineSize     = 60;
        ReloadTime       = 3.0f;
        BulletDamage     = 30f;    // double pistol (15 × 2)
        StartReserveAmmo = 60;
        MaxReserveAmmo   = 180;
        BulletKnockback  = 50f;
        base._Ready();
    }
}
