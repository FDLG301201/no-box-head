using Godot;

namespace NoBoxHead;

/// <summary>
/// Semi-automatic pistol: 12 rounds, 1.5 s reload, moderate damage.
/// </summary>
public partial class Pistol : Weapon
{
    public override void _Ready()
    {
        FireRate = 0.35f;
        MagazineSize = 12;
        ReloadTime = 1.5f;
        BulletDamage = 15f;
        base._Ready();
    }
}
