using Godot;

namespace NoBoxHead;

/// <summary>
/// Semi-automatic pistol. 12-round magazine, 12 reserve, 1.5 s reload.
/// </summary>
public partial class Pistol : Weapon
{
    public override string WeaponName => "Pistol";

    public override void _Ready()
    {
        FireRate         = 0.35f;
        MagazineSize     = 12;
        ReloadTime       = 1.5f;
        BulletDamage     = 15f;
        StartReserveAmmo = 12;
        MaxReserveAmmo   = 60;
        BulletKnockback  = 120f;
        base._Ready();
    }
}
