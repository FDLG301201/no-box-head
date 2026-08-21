using Godot;

namespace NoBoxHead;

/// <summary>
/// Places a ProximityMine a short distance in front of the player, sliding to the nearest free
/// spot when the ideal one is blocked — same search pattern as BarrelWeapon.FindPlacementSpot,
/// duplicated here rather than shared because a mine's footprint/placement rules are its own
/// (no nav-mesh carving, no ArenaRef registration).
/// </summary>
public partial class MineWeapon : Weapon
{
    public override string WeaponName => "Proximity Mine";
    protected override string FireSound => AudioManager.BarrelPlace;

    private const float PlacementDistance = 50f;
    private const float MineFootprint     = 20f; // clearance used only for the placement query

    private PackedScene? _mineScene;

    public override void _Ready()
    {
        FireRate         = 0.7f;
        MagazineSize     = 3;
        ReloadTime       = 0f;
        BulletDamage     = 160f;
        StartReserveAmmo = 0;
        MaxReserveAmmo   = 6;
        BulletKnockback  = 300f;
        base._Ready();

        _mineScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Mine.tscn");
    }

    // Same contract as BarrelWeapon: refuse (no ammo spent, no cooldown) only when nowhere fits.
    public override void TryShoot(Vector2 origin, Vector2 direction)
    {
        if (FindPlacementSpot(origin, direction) == null) return;
        base.TryShoot(origin, direction);
    }

    protected override void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        if (_mineScene == null) return;
        var spot = FindPlacementSpot(origin, direction);
        if (spot == null) return;

        Place(spot.Value, cosmetic: false);

        // Mirrors the resolved spot rather than re-running placement search on every peer —
        // avoids the two peers picking different free slots for what should be the same mine.
        if (NetworkManager.IsNetworked)
            Rpc(MethodName.PlaceMineRpc, spot.Value);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    private void PlaceMineRpc(Vector2 spot) => Place(spot, cosmetic: true);

    private void Place(Vector2 spot, bool cosmetic)
    {
        if (_mineScene == null) return;
        var mine = _mineScene.Instantiate<ProximityMine>();
        mine.Damage        = BulletDamage;
        mine.KnockbackForce = BulletKnockback;
        mine.Cosmetic       = cosmetic;
        mine.GlobalPosition = spot;
        (BulletContainer ?? GetTree().Root).AddChild(mine);
    }

    private Vector2? FindPlacementSpot(Vector2 origin, Vector2 direction)
    {
        var aim   = direction.Normalized();
        var ideal = origin + aim * PlacementDistance;
        if (CanPlaceAt(ideal)) return ideal;

        const int   rings        = 5;
        const float ringStep     = MineFootprint * 0.9f;
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
    /// anything outside the arena bounds — same layers BarrelWeapon checks.
    /// </summary>
    private bool CanPlaceAt(Vector2 point)
    {
        float half = MineFootprint / 2f;
        if (point.X < half || point.Y < half ||
            point.X > ArenaLayouts.ArenaW - half || point.Y > ArenaLayouts.ArenaH - half)
            return false;

        var space = (GetParent() as Node2D)?.GetWorld2D()?.DirectSpaceState;
        if (space == null) return true;

        var query = new PhysicsShapeQueryParameters2D
        {
            Shape             = new RectangleShape2D { Size = new Vector2(MineFootprint, MineFootprint) },
            Transform         = new Transform2D(0f, point),
            CollisionMask     = 1 | 4,
            CollideWithBodies = true,
            CollideWithAreas  = false,
        };
        return space.IntersectShape(query, 1).Count == 0;
    }
}
