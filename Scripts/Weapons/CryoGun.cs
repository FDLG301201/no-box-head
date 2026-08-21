using Godot;

namespace NoBoxHead;

/// <summary>
/// Fires a slow cryo bolt that barely damages but locks its target down: EnemyBase.ApplySlow
/// cuts the struck enemy's move speed and tints it blue for a few seconds. Same mirroring
/// pattern as RocketLauncher/MineWeapon — the shooter's own bolt resolves the hit, everyone
/// else sees a cosmetic copy.
/// </summary>
public partial class CryoGun : Weapon
{
    public override string WeaponName => "Cryo Gun";
    protected override string FireSound => AudioManager.CryoGun;
    // No BulletColor override here: this weapon fires a CryoProjectile, not a Bullet (see Fire()
    // below), and CryoProjectile._Ready already paints its own icy-blue ColorRect.

    private PackedScene? _boltScene;

    public override void _Ready()
    {
        FireRate         = 0.6f;
        MagazineSize     = 6;
        ReloadTime       = 1.8f;
        BulletDamage     = 8f;
        StartReserveAmmo = 12;
        MaxReserveAmmo   = 36;
        BulletKnockback  = 60f;
        base._Ready();

        _boltScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/CryoBolt.tscn");
    }

    protected override void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        Fire(origin, direction, cosmetic: false);

        // Without this only the shooter would ever see the bolt fly — every other projectile
        // weapon here mirrors itself the same way (GrenadeLauncher.ThrowGrenadeRpc).
        if (NetworkManager.IsNetworked)
            Rpc(MethodName.FireCryoRpc, origin, direction);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    private void FireCryoRpc(Vector2 origin, Vector2 direction) =>
        Fire(origin, direction, cosmetic: true);

    private void Fire(Vector2 origin, Vector2 direction, bool cosmetic)
    {
        if (_boltScene == null) return;
        var bolt = _boltScene.Instantiate<CryoProjectile>();
        bolt.Damage         = BulletDamage;
        bolt.KnockbackForce = BulletKnockback;
        bolt.Cosmetic       = cosmetic;
        (BulletContainer ?? GetTree().Root).AddChild(bolt);
        bolt.Init(origin, direction);
    }
}
