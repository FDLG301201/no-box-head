using Godot;

namespace NoBoxHead;

/// <summary>
/// The Butcher's signature shot: a slow shell that flies for a short fuse (or on impact) then
/// bursts into a star of <see cref="BurstCount"/> bullets fired in evenly-spaced directions.
/// Deliberately slow and telegraphed (it visibly swells as the fuse burns down) — the threat is
/// the burst pattern, not the shell itself, so a player who reads it has time to walk out of the
/// star before it forms.
///
/// Mirrors DemonProjectile/GrenadeProjectile's Cosmetic pattern: a cosmetic shell still runs its
/// own fuse and detonation locally on every peer, so the burst bullets it spawns are themselves
/// cosmetic and never double the host's damage.
/// </summary>
public partial class BossShell : Area2D
{
    [Export] public float Speed            = 90f;
    [Export] public float FuseTime         = 1.6f;
    [Export] public int   BurstCount       = 8;
    [Export] public float BurstBulletSpeed = 260f;
    [Export] public float BurstDamage      = 16f;
    [Export] public float BurstLifetime    = 2.2f;

    /// <summary>See DemonProjectile.Cosmetic — only the host's copy's burst deals damage.</summary>
    public bool Cosmetic;

    private Vector2 _direction;
    private bool    _detonated;
    private ColorRect? _visual;

    public void Init(Vector2 origin, Vector2 direction)
    {
        GlobalPosition = origin;
        _direction     = direction.Normalized();
        Rotation       = _direction.Angle() + Mathf.Pi / 2f;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask  = 3; // walls (1) + players (2)

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 8f } });
        _visual = new ColorRect
        {
            Color    = new Color(0.55f, 0.05f, 0.55f),
            Size     = new Vector2(16, 16),
            Position = new Vector2(-8, -8),
            PivotOffset = new Vector2(8, 8),
        };
        AddChild(_visual);

        // Telegraph: the shell visibly swells as its fuse burns down, so the burst point and
        // timing are readable before the star actually forms.
        var tween = CreateTween();
        tween.TweenProperty(_visual, "scale", new Vector2(1.9f, 1.9f), FuseTime)
             .SetTrans(Tween.TransitionType.Sine);

        // Spawning the burst (new Area2Ds with monitoring) from inside a BodyEntered callback
        // throws "Can't change this state while flushing queries" — defer it.
        BodyEntered += _ => CallDeferred(MethodName.Detonate);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_detonated) return;

        GlobalPosition += _direction * Speed * (float)delta;

        FuseTime -= (float)delta;
        if (FuseTime <= 0f) CallDeferred(MethodName.Detonate);
    }

    private void Detonate()
    {
        if (_detonated || !IsInstanceValid(this)) return;
        _detonated = true;
        SetPhysicsProcess(false);
        Monitoring = false; // stop re-entering here while we're mid-teardown
        Visible = false;

        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/BossBurstBullet.tscn");
        if (scene != null)
        {
            // Reuse whatever container this shell itself was added to (bullets container on the
            // host, same on clients) rather than tracking a separate reference.
            var container = GetParent();
            for (int i = 0; i < BurstCount; i++)
            {
                Vector2 dir = Vector2.FromAngle(Mathf.Tau * i / BurstCount);
                var bullet = scene.Instantiate<BossBurstBullet>();
                bullet.Cosmetic = Cosmetic;
                bullet.Damage   = BurstDamage;
                bullet.Speed    = BurstBulletSpeed;
                bullet.Lifetime = BurstLifetime;
                container?.AddChild(bullet);
                // Offset outward along its own direction so it never spawns inside whatever the
                // shell just hit (the "spawned exactly at impact starts inside the collider" trap).
                bullet.Init(GlobalPosition + dir * 14f, dir);
            }
        }

        AudioManager.Instance?.Play(AudioManager.Explosion, 0.75f, 0.05f);
        QueueFree();
    }
}
