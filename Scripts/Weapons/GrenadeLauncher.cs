using Godot;

namespace NoBoxHead;

/// <summary>
/// Deliberately "broken" reward weapon: absurd splash damage and knockback for a single
/// point-blank grenade. Starts with only 2 total charges; ammo packs can refill the reserve,
/// but grenade ammo drops at a much lower rate than every other weapon (see
/// ScoreManager.AmmoDropWeight), so it stays scarce even late-game.
/// </summary>
public partial class GrenadeLauncher : Weapon
{
    public override string WeaponName => "Grenade";

    private PackedScene? _grenadeScene;

    public override void _Ready()
    {
        FireRate         = 0.6f;
        MagazineSize     = 1;
        ReloadTime       = 2.5f;
        BulletDamage     = 90f;
        StartReserveAmmo = 1; // 1 loaded + 1 reserve = 2 total throws to start
        MaxReserveAmmo   = 2; // ammo packs can stock up to 1 loaded + 2 reserve = 3
        BulletKnockback  = 260f;
        base._Ready();

        _grenadeScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Grenade.tscn");
    }

    protected override void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        if (_grenadeScene == null) return;
        var grenade = _grenadeScene.Instantiate<GrenadeProjectile>();
        grenade.Damage         = BulletDamage;
        grenade.KnockbackForce = BulletKnockback;
        (BulletContainer ?? GetTree().Root).AddChild(grenade);
        grenade.Init(origin, direction);
    }
}
