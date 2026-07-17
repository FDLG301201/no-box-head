using Godot;

namespace NoBoxHead;

/// <summary>
/// Utility "weapon": drops a destructible barrel a short distance in front of the player to
/// block a path. Reuses Weapon's ammo/cooldown machinery, just spawns a Barrel instead of a
/// bullet. Starts with a handful of charges and refills from ammo packs at the same rate as
/// any other weapon (see ScoreManager.AmmoDropWeight).
/// </summary>
public partial class BarrelWeapon : Weapon
{
    public override string WeaponName => "Barrel";

    private const float PlacementDistance = 46f;

    public Arena? ArenaRef { get; set; }
    public Node?  ObstacleContainer { get; set; }

    private PackedScene? _barrelScene;

    public override void _Ready()
    {
        FireRate         = 0.8f;
        MagazineSize     = 4;
        ReloadTime       = 0f;
        BulletDamage     = 0f;
        StartReserveAmmo = 0; // starts with just the 4 in the "magazine"
        MaxReserveAmmo   = 8; // ammo packs can stock up to 4 + 8 = 12 placements
        BulletKnockback  = 0f;
        base._Ready();

        _barrelScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Barrel.tscn");
    }

    protected override void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        if (_barrelScene == null) return;
        var barrel = _barrelScene.Instantiate<Barrel>();
        barrel.ArenaRef       = ArenaRef;
        barrel.GlobalPosition = origin + direction.Normalized() * PlacementDistance;
        (ObstacleContainer ?? GetTree().Root).AddChild(barrel);
    }
}
