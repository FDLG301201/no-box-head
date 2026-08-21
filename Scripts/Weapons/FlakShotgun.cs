using Godot;

namespace NoBoxHead;

/// <summary>
/// Shotgun variant: same pellet cone as Shotgun, but each pellet bursts into a handful of
/// short-range fragments on whatever it hits (or the wall behind it), adding extra close-range
/// damage without changing the base pellet's own falloff. Fragments never fragment again.
/// </summary>
public partial class FlakShotgun : Weapon
{
    private const int   PelletCount      = 5;
    private const float SpreadDegrees    = 30f;
    private const float PelletMaxRange   = 260f;
    private const float PelletMinDamage  = 0.15f;
    private const float PelletFalloff    = 0.3f;

    private const int   FragmentCount    = 3;
    private const float FragmentSpread   = 100f; // degrees, wide burst
    private const float FragmentRange    = 90f;  // short — this is a close-range bonus, not a second shotgun
    private const float FragmentDamageFactor = 0.35f; // per fragment, of the pellet's own damage
    // How far past the impact point a fragment spawns before it starts moving. Measured by probe
    // (Tests/WeaponProbe, deleted after use): a straight-line fragment spawned only 10px past the
    // hit enemy's centre was still inside that enemy's own collider and immediately re-triggered
    // BodyEntered on it instead of continuing toward the next target.
    private const float FragmentSpawnPush = 24f;

    public override string WeaponName => "Flak Shotgun";
    protected override string FireSound => AudioManager.Shotgun;
    // Cool steel-grey — reads as metal shrapnel rather than the warm amber of the plain Shotgun,
    // so the two pellet spreads are distinguishable at a glance.
    protected override Color BulletColor => new Color(0.7f, 0.78f, 0.85f);

    public override void _Ready()
    {
        FireRate         = 0.9f;
        MagazineSize     = 8;
        ReloadTime       = 2.2f;
        BulletDamage     = 25f;
        StartReserveAmmo = 16;
        MaxReserveAmmo   = 64;
        base._Ready();
    }

    protected override void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        if (BulletScene == null) return;
        float baseAngle = direction.Angle();

        for (int i = 0; i < PelletCount; i++)
        {
            float t      = (float)i / (PelletCount - 1) - 0.5f;
            float offset = t * Mathf.DegToRad(SpreadDegrees);
            var   dir    = Vector2.FromAngle(baseAngle + offset);

            var pellet = BulletScene.Instantiate<Bullet>();
            pellet.Damage            = BulletDamage;
            pellet.MaxDistance       = PelletMaxRange;
            pellet.MinDamageFactor   = PelletMinDamage;
            pellet.FalloffStartRatio = PelletFalloff;
            pellet.KnockbackForce    = 45f;
            pellet.Color             = BulletColor;
            // Fires regardless of Cosmetic, so a mirrored pellet on another peer also bursts —
            // no extra RPC needed, the fragments ride along on the already-mirrored tracer.
            // Deferred: OnImpact runs from inside the pellet's own BodyEntered physics callback,
            // and Godot refuses to register a brand-new Area2D's collision shape while the
            // physics server is still flushing that same query — spawning immediately silently
            // produced fragments with no working collider. CallDeferred pushes the actual spawn
            // to right after the physics step finishes.
            pellet.OnImpact          = (pos, inDir) =>
                CallDeferred(MethodName.SpawnFragmentsDeferred, pos, inDir, pellet.Cosmetic);
            (BulletContainer ?? GetTree().Root).AddChild(pellet);
            pellet.Init(origin, dir, BulletDamage);

            BroadcastTracer(origin, dir, PelletMaxRange, pellet.Speed);
        }
    }

    private void SpawnFragmentsDeferred(Vector2 origin, Vector2 incomingDir, bool cosmetic)
    {
        if (BulletScene == null) return;
        float baseAngle = incomingDir.Angle();
        float fragDamage = BulletDamage * FragmentDamageFactor;

        for (int i = 0; i < FragmentCount; i++)
        {
            float t      = FragmentCount == 1 ? 0f : (float)i / (FragmentCount - 1) - 0.5f;
            float offset = t * Mathf.DegToRad(FragmentSpread);
            var   dir    = Vector2.FromAngle(baseAngle + offset);

            var frag = BulletScene.Instantiate<Bullet>();
            frag.Damage      = fragDamage;
            frag.MaxDistance = FragmentRange;
            frag.KnockbackForce = 20f;
            frag.Color       = BulletColor;
            frag.Cosmetic    = cosmetic; // matches the parent pellet — only the real pellet's
                                         // fragments deal damage, the mirrored one's are visual only
            (BulletContainer ?? GetTree().Root).AddChild(frag);
            // Nudged out of the pellet's own collider: spawning exactly at the impact point (still
            // overlapping the enemy that was just hit) let a fragment immediately re-trigger
            // BodyEntered on that SAME enemy instead of flying on to the next one — measured with
            // a two-enemy probe (Tests/WeaponProbe, deleted after use): without the offset the
            // second enemy took zero fragment damage no matter how close it stood.
            frag.Init(origin + dir * FragmentSpawnPush, dir, fragDamage);
        }
    }
}
