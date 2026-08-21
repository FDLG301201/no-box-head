using Godot;

namespace NoBoxHead;

/// <summary>
/// Deploys an auto-firing Turret a short distance in front of the player. Placement search is
/// the same "find a free spot nearby" pattern as MineWeapon/BarrelWeapon, duplicated rather
/// than shared for the same reason MineWeapon duplicates it: a turret's footprint/placement
/// rules are its own. The resolved spot is mirrored to every peer (like ProximityMine) so
/// everyone's copy sits in the same place — Turret itself then decides, per peer, whether it's
/// the one that simulates (see Turret._isHost).
/// </summary>
public partial class TurretWeapon : Weapon
{
    public override string WeaponName => "Turret";
    protected override string FireSound => AudioManager.BarrelPlace;

    private const float PlacementDistance = 50f;
    private const float TurretFootprint   = 26f;

    private PackedScene? _turretScene;

    public override void _Ready()
    {
        FireRate         = 0.6f;
        MagazineSize     = 1;
        ReloadTime       = 0f;
        BulletDamage     = 14f; // per-shot turret damage
        StartReserveAmmo = 0;
        MaxReserveAmmo   = 3;   // up to 3 turrets stocked from ammo pickups
        BulletKnockback  = 40f;
        base._Ready();

        _turretScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Turret.tscn");
    }

    // Same contract as BarrelWeapon/MineWeapon: refuse (no ammo spent, no cooldown) only when
    // nowhere fits.
    public override void TryShoot(Vector2 origin, Vector2 direction)
    {
        if (FindPlacementSpot(origin, direction) == null) return;
        base.TryShoot(origin, direction);
    }

    protected override void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        var spot = FindPlacementSpot(origin, direction);
        if (spot == null) return;

        Place(spot.Value);

        if (NetworkManager.IsNetworked)
            Rpc(MethodName.PlaceTurretRpc, spot.Value);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    private void PlaceTurretRpc(Vector2 spot) => Place(spot);

    private void Place(Vector2 spot)
    {
        if (_turretScene == null) return;
        var turret = _turretScene.Instantiate<Turret>();
        turret.Damage         = BulletDamage;
        turret.GlobalPosition = spot;
        (BulletContainer ?? GetTree().Root).AddChild(turret);
    }

    private Vector2? FindPlacementSpot(Vector2 origin, Vector2 direction)
    {
        var aim   = direction.Normalized();
        var ideal = origin + aim * PlacementDistance;
        if (CanPlaceAt(ideal)) return ideal;

        const int   rings        = 5;
        const float ringStep     = TurretFootprint * 0.9f;
        const int   stepsPerHalf = 6;

        float baseAngle = aim.Angle();
        for (int ring = 1; ring <= rings; ring++)
        {
            float radius = ring * ringStep;
            for (int step = 0; step <= stepsPerHalf; step++)
            {
                float spread = Mathf.Pi * step / stepsPerHalf;
                foreach (float angle in step == 0
                             ? new[] { baseAngle }
                             : new[] { baseAngle + spread, baseAngle - spread })
                {
                    var candidate = ideal + Vector2.FromAngle(angle) * radius;
                    if (CanPlaceAt(candidate)) return candidate;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Rejects spots overlapping walls/barrels (layer 1) or a living enemy (layer 4), and
    /// anything outside the arena bounds — same layers MineWeapon/BarrelWeapon check.
    /// </summary>
    private bool CanPlaceAt(Vector2 point)
    {
        float half = TurretFootprint / 2f;
        if (point.X < half || point.Y < half ||
            point.X > ArenaLayouts.ArenaW - half || point.Y > ArenaLayouts.ArenaH - half)
            return false;

        var space = (GetParent() as Node2D)?.GetWorld2D()?.DirectSpaceState;
        if (space == null) return true;

        var query = new PhysicsShapeQueryParameters2D
        {
            Shape             = new RectangleShape2D { Size = new Vector2(TurretFootprint, TurretFootprint) },
            Transform         = new Transform2D(0f, point),
            CollisionMask     = 1 | 4,
            CollideWithBodies = true,
            CollideWithAreas  = false,
        };
        return space.IntersectShape(query, 1).Count == 0;
    }
}
