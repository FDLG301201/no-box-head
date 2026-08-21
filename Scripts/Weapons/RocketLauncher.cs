using Godot;

namespace NoBoxHead;

/// <summary>
/// Direct-fire splash weapon: faster and flatter than the Grenade Launcher's lob, trading its
/// huge point-blank payoff for a reliable mid-range hit plus a strong bonus on whatever it
/// actually strikes. Reuses RocketProjectile for the flight/explosion, same mirroring pattern
/// as GrenadeLauncher.
/// </summary>
public partial class RocketLauncher : Weapon
{
    public override string WeaponName => "Rocket Launcher";
    protected override string FireSound => AudioManager.Explosion;

    private PackedScene? _rocketScene;

    public override void _Ready()
    {
        FireRate         = 1.1f;
        MagazineSize     = 2;
        ReloadTime       = 2.2f;
        BulletDamage     = 90f;   // splash damage; RocketProjectile.DirectHitBonus adds on top
        StartReserveAmmo = 4;
        MaxReserveAmmo   = 12;
        BulletKnockback  = 320f;
        base._Ready();

        _rocketScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Rocket.tscn");
    }

    protected override void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        Fire(origin, direction, cosmetic: false);

        // Without this only the shooter would ever see the rocket fly — every other splash
        // weapon here mirrors itself the same way (GrenadeLauncher.ThrowGrenadeRpc).
        if (NetworkManager.IsNetworked)
            Rpc(MethodName.FireRocketRpc, origin, direction);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    private void FireRocketRpc(Vector2 origin, Vector2 direction) =>
        Fire(origin, direction, cosmetic: true);

    private void Fire(Vector2 origin, Vector2 direction, bool cosmetic)
    {
        if (_rocketScene == null) return;
        var rocket = _rocketScene.Instantiate<RocketProjectile>();
        rocket.Damage        = BulletDamage;
        rocket.KnockbackForce = BulletKnockback;
        rocket.Cosmetic       = cosmetic;
        (BulletContainer ?? GetTree().Root).AddChild(rocket);
        rocket.Init(origin, direction);
    }
}
