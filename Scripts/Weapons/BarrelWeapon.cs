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
    protected override string FireSound => AudioManager.BarrelPlace;

    private const float PlacementDistance = 46f;
    // Slightly larger than Barrel.Size (30) so barrels never end up flush against each other.
    private const float BarrelFootprint   = 34f;

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

    // Only refuse the shot when there is genuinely nowhere nearby to put the barrel, so a
    // blocked spot doesn't burn a charge or start the cooldown (Weapon.TryShoot spends ammo
    // around SpawnBullet). Otherwise it slides to the closest free slot.
    public override void TryShoot(Vector2 origin, Vector2 direction)
    {
        if (FindPlacementSpot(origin, direction) == null) return;
        base.TryShoot(origin, direction);
    }

    protected override void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        if (_barrelScene == null) return;
        var spot = FindPlacementSpot(origin, direction);
        if (spot == null) return;

        var barrel = _barrelScene.Instantiate<Barrel>();
        barrel.ArenaRef       = ArenaRef;
        barrel.GlobalPosition = spot.Value;
        (ObstacleContainer ?? GetTree().Root).AddChild(barrel);
    }

    /// <summary>
    /// Returns the ideal spot in front of the player, or — when that's taken (a wall, another
    /// barrel…) — the nearest free spot around it, searched in widening rings. Angles are
    /// tried alternating either side of the aim direction, so a barrel dropped onto an
    /// existing one lands beside it rather than being refused. Null if nothing fits.
    /// </summary>
    private Vector2? FindPlacementSpot(Vector2 origin, Vector2 direction)
    {
        var aim   = direction.Normalized();
        var ideal = origin + aim * PlacementDistance;
        if (CanPlaceAt(ideal)) return ideal;

        const int   rings      = 5;
        const float ringStep   = BarrelFootprint * 0.8f;
        const int   stepsPerHalf = 6; // 6 offsets each side → 30° apart around the ring

        float baseAngle = aim.Angle();
        for (int ring = 1; ring <= rings; ring++)
        {
            float radius = ring * ringStep;
            for (int step = 0; step <= stepsPerHalf; step++)
            {
                float spread = Mathf.Pi * step / stepsPerHalf;
                // step 0 is straight ahead; afterwards probe both sides before widening.
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
    /// Rejects spots overlapping walls, arena obstacles, other barrels (all collision layer 1)
    /// or a living enemy (layer 4), and anything outside the arena bounds.
    /// </summary>
    private bool CanPlaceAt(Vector2 point)
    {
        float half = BarrelFootprint / 2f;
        if (point.X < half || point.Y < half ||
            point.X > ArenaLayouts.ArenaW - half || point.Y > ArenaLayouts.ArenaH - half)
            return false;

        var space = (GetParent() as Node2D)?.GetWorld2D()?.DirectSpaceState;
        if (space == null) return true; // no physics context yet — don't block placement

        var query = new PhysicsShapeQueryParameters2D
        {
            Shape             = new RectangleShape2D { Size = new Vector2(BarrelFootprint, BarrelFootprint) },
            Transform         = new Transform2D(0f, point),
            CollisionMask     = 1 | 4, // static geometry + barrels, and enemies
            CollideWithBodies = true,
            CollideWithAreas  = false,
        };
        return space.IntersectShape(query, 1).Count == 0;
    }
}
