using Godot;

namespace NoBoxHead;

/// <summary>
/// Utility "weapon": drops a destructible barrel at the player's own position to block a path.
/// Reuses Weapon's ammo/cooldown machinery, just spawns a Barrel instead of a bullet. Starts
/// with a handful of charges and refills from ammo packs at the same rate as any other weapon
/// (see ScoreManager.AmmoDropWeight).
/// </summary>
public partial class BarrelWeapon : Weapon
{
    public override string WeaponName => "Barrel";
    protected override string FireSound => AudioManager.BarrelPlace;

    // Equal to Barrel.Size (30) minus a hair, so a row of barrels CAN end up flush against each
    // other. Measured by probe (see Tests/ProbeBarrelGap, deleted after use): a shape query of
    // exactly 30x30 centred 30px from an existing barrel — i.e. perfectly edge-to-edge — still
    // reports an overlap (1 hit), while 29.99 already reports clear. The cutoff sits precisely
    // at 30.0 with no real physics margin to speak of, but placements below aren't computed at
    // that exact float value — they're built from trig (angle/radius) — so a small safety
    // margin below the measured threshold avoids flakiness from floating-point drift.
    private const float BarrelFootprint   = 29.5f;

    // World-anchored grid used to line up flush walls. Placement is otherwise continuous
    // (derived from a float aim angle), so two barrels dropped a beat apart rarely land on
    // exactly the same line even when both candidate spots are individually valid. Snapping the
    // final spot onto a fixed BarrelSize-wide grid (only when the snapped spot is ALSO valid —
    // see FindPlacementSpot) pulls placements into alignment without making placement feel rigid,
    // since the unsnapped spot still works as a fallback whenever snapping isn't possible.
    private const float GridCell = 30f; // matches Barrel.Size, not the shrunk query footprint

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
        if (FindPlacementSpot(origin) == null) return;
        base.TryShoot(origin, direction);
    }

    protected override void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        if (_barrelScene == null) return;
        var spot = FindPlacementSpot(origin);
        if (spot == null) return;

        var barrel = _barrelScene.Instantiate<Barrel>();
        barrel.ArenaRef       = ArenaRef;
        barrel.GlobalPosition = spot.Value;
        (ObstacleContainer ?? GetTree().Root).AddChild(barrel);
    }

    /// <summary>
    /// Returns the player's own position, or — when that spot is genuinely taken (a wall,
    /// another barrel, an enemy standing there) — the nearest free spot around it, searched in
    /// widening rings. Deliberately ignores aim direction, and the ring sweep is a fixed one
    /// rather than aim-relative, so the same blocked situation resolves the same way in every
    /// aim mode.
    ///
    /// The player's own body is intentionally NOT an obstacle here: a barrel is meant to drop
    /// underneath you. It used to shove you sideways when it did, because a solid barrel
    /// spawning inside your collision circle leaves the physics engine no other way to resolve
    /// the overlap. That is fixed in Barrel itself, which ignores collisions with anyone it
    /// spawned on top of until they step clear — see Barrel.SoftSpawn. Adding the player layer
    /// to CanPlaceAt would "fix" it by never dropping the barrel underfoot at all, which is the
    /// behaviour the owner did not want.
    /// </summary>
    private Vector2? FindPlacementSpot(Vector2 origin)
    {
        if (CanPlaceAt(origin)) return SnapIfValid(origin);

        const int   rings        = 5;
        const float ringStep     = BarrelFootprint * 0.8f;
        const int   stepsPerHalf = 6; // 6 offsets each side -> 30 degrees apart around the ring

        for (int ring = 1; ring <= rings; ring++)
        {
            float radius = ring * ringStep;
            for (int step = 0; step <= stepsPerHalf; step++)
            {
                float spread = Mathf.Pi * step / stepsPerHalf;
                // step 0 is due east; afterwards probe both sides before widening.
                foreach (float angle in step == 0 ? new[] { 0f } : new[] { spread, -spread })
                {
                    var candidate = origin + Vector2.FromAngle(angle) * radius;
                    if (CanPlaceAt(candidate)) return SnapIfValid(candidate);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Pulls an already-valid candidate onto the world-anchored barrel grid when that snapped
    /// spot is itself still valid, so repeated placements along a rough line settle onto the
    /// same row/column instead of drifting by whatever the aim angle happened to be. Falls back
    /// to the original (already-verified) spot otherwise — snapping is a nicety, never a reason
    /// to refuse a placement that would otherwise succeed.
    /// </summary>
    private Vector2 SnapIfValid(Vector2 candidate)
    {
        var snapped = new Vector2(
            Mathf.Round(candidate.X / GridCell) * GridCell,
            Mathf.Round(candidate.Y / GridCell) * GridCell);
        return CanPlaceAt(snapped) ? snapped : candidate;
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
            // Players (layer 2) are deliberately absent: a barrel is supposed to be
            // placeable underfoot. Barrel.SoftSpawn stops that from shoving anyone.
            CollisionMask     = 1 | 4, // static geometry + barrels, and enemies
            CollideWithBodies = true,
            CollideWithAreas  = false,
        };
        return space.IntersectShape(query, 1).Count == 0;
    }
}
