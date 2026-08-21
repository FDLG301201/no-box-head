using Godot;

namespace NoBoxHead;

/// <summary>
/// Demon enemy. Tougher than zombie, slower, navigates around walls, and fires projectiles.
/// Appears from wave 3. Worth 25 pts on kill.
///
/// Unlike the melee types this one overrides <see cref="_PhysicsProcess"/> outright instead of
/// leaning on EnemyBase's chase loop: it holds position at ShootRange rather than closing to
/// melee, only separates from the pack while it is actually moving, and measures "am I stuck"
/// against its full walk speed. Everything downstream of movement — damage RPCs, death, drops,
/// health bar, knockback — is inherited.
/// </summary>
public partial class Demon : EnemyBase
{
    [Export] public float ShootRange    = 250f;
    [Export] public float ShootCooldown = 2.5f;

    private float _shootTimer;
    private Node? _projectileContainer;

    public Demon()
    {
        MoveSpeed      = 35f;
        MaxHealth      = 80f;
        AttackDamage   = 15f;
        AttackCooldown = 1.2f;
        AttackRange    = 30f;
    }

    // Paths in coarser steps than the melee types — it only needs to get roughly into firing
    // position, not hug a corner.
    protected override float NavPathDesiredDistance => 12f;

    protected override Color FlashColor    => new Color(1f, 0.6f, 0.6f);
    protected override Color DeathModulate => Colors.DarkGray;

    protected override int   ScoreValue       => 25;
    protected override float BloodPoolScale   => 1.35f;
    protected override float HealthDropChance => 0.08f;

    public void SetProjectileContainer(Node container) => _projectileContainer = container;

    protected override void PlayDeathSound() =>
        AudioManager.Instance?.Play(AudioManager.EnemyDeath, 0.9f, 0.06f);

    /// <summary>Always drops, and a bigger pack than the melee types — no chance roll.</summary>
    protected override void DropAmmo()
    {
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/AmmoPack.tscn");
        if (scene == null) return;
        var pack = scene.Instantiate<AmmoPack>();
        pack.AmmoAmount     = 6;
        pack.WeaponType     = ScoreManager.Instance?.GetRandomUnlockedAmmoType() ?? "Pistol";
        pack.GlobalPosition = GlobalPosition;
        GetParent()?.AddChild(pack);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isHost || !IsAlive) return;

        var target = GameManager.Instance?.GetNearestPlayer(GlobalPosition);
        if (target == null || !target.IsAlive) return;

        float   dist = GlobalPosition.DistanceTo(target.GlobalPosition);
        Vector2 dir  = (target.GlobalPosition - GlobalPosition).Normalized();

        // If barrels have walled off the player, path to the nearest one and smash it
        // instead of standing still shooting fireballs into a wall.
        bool playerReachable = _navAgent == null || _navAgent.IsTargetReachable();
        if (!playerReachable)
            _targetBarrel = (_targetBarrel != null && IsInstanceValid(_targetBarrel) && _targetBarrel.IsAlive)
                ? _targetBarrel
                : FindNearestBarrel();
        else
            _targetBarrel = null;

        Vector2 navTarget = _targetBarrel?.GlobalPosition ?? target.GlobalPosition;
        float   navTargetDist = GlobalPosition.DistanceTo(navTarget);

        // "In range" on a straight-line distance check means nothing if a wall sits between —
        // the fireball would just splash against it. Treat a blocked line of fire the same as
        // being too far away, so the demon keeps moving (the nav mesh routes it around the
        // obstacle) instead of freezing next to a wall it can't shoot through.
        bool blockedLineOfFire = _targetBarrel == null && dist <= ShootRange &&
                                  !HasLineOfSight(target.GlobalPosition);

        // Navigate toward the target only while outside preferred shoot range (skipped
        // entirely while chasing a barrel — it has no ranged attack of its own).
        if (_targetBarrel != null || dist > ShootRange * 0.65f || blockedLineOfFire)
        {
            Vector2 navDir = _targetBarrel != null ? (navTarget - GlobalPosition).Normalized() : dir;
            if (_navAgent != null)
            {
                _navAgent.TargetPosition = navTarget;
                if (!_navAgent.IsNavigationFinished())
                {
                    var nextPos = _navAgent.GetNextPathPosition();
                    if (nextPos.DistanceTo(GlobalPosition) > 1f)
                        navDir = (nextPos - GlobalPosition).Normalized();
                }
            }
            Velocity = navDir * MoveSpeed;

            // Separation from other enemies.
            foreach (var node in GetTree().GetNodesInGroup("enemies"))
            {
                if (node is Node2D other && other != this && IsInstanceValid(other))
                {
                    float d = GlobalPosition.DistanceTo(other.GlobalPosition);
                    if (d < SeparationRadius && d > 0f)
                        Velocity += (GlobalPosition - other.GlobalPosition).Normalized() * (SeparationRadius - d) * 0.5f;
                }
            }
        }
        else
        {
            Velocity = Vector2.Zero;
        }

        // Deliberately no CrowdSeparation.AwayFromPlayers here (unlike EnemyBase's melee
        // types). This demon is a ranged attacker that should hold its ground and keep firing
        // even at point-blank; the shared "yield so a player can push through" nudge shoved it
        // back out to AttackRange the instant the player closed inside that radius, which read
        // as backing away right when it should have been standing and shooting.

        // Apply and decay knockback impulse (always, even while stationary).
        if (_knockback.LengthSquared() > 1f)
        {
            Velocity += _knockback;
            _knockback *= KnockbackDecay;
        }
        else
        {
            _knockback = Vector2.Zero;
        }

        // Captured before MoveAndSlide can shorten Velocity on contact, so holding position to
        // fire (Velocity already zero) is never misread as being stuck. This used to compare
        // against a constant (MoveSpeed * delta) instead, which is never zero — so the demon
        // deliberately standing still to shoot got treated as "stuck" every single frame and was
        // kicked sideways by the recovery nudge below every StuckWindow seconds even with a
        // completely clear shot, which is exactly the erratic drifting the owner reported.
        float intendedDist = Velocity.Length() * (float)delta;

        MoveAndSlide();

        // ── Stuck recovery ────────────────────────────────────────────────────
        float movedDist    = GlobalPosition.DistanceTo(_prevPosition);
        float expectedDist = intendedDist;
        if (expectedDist > 0f && movedDist < expectedDist * StuckMinRatio)
        {
            _stuckTimer += (float)delta;
            if (_stuckTimer >= StuckWindow)
            {
                Vector2 navDir2 = (navTarget - GlobalPosition).Normalized();
                if (_navAgent != null && !_navAgent.IsNavigationFinished())
                    navDir2 = (_navAgent.GetNextPathPosition() - GlobalPosition).Normalized();
                Velocity = navDir2.Rotated(_stuckSide * Mathf.Pi * 0.5f) * MoveSpeed;
                MoveAndSlide();
                _stuckTimer = 0f;
                _stuckSide  = -_stuckSide;
            }
        }
        else
        {
            _stuckTimer = 0f;
        }
        _prevPosition = GlobalPosition;

        // Fixed front-facing pose — mirror horizontally instead of rotating the body.
        Vector2 faceDir = _targetBarrel != null ? navTarget - GlobalPosition : dir;
        if (_visual != null && Mathf.Abs(faceDir.X) > 5f)
            _visual.FlipH = faceDir.X < 0f;

        _attackTimer -= (float)delta;
        _shootTimer  -= (float)delta;

        if (_targetBarrel != null)
        {
            if (navTargetDist <= BarrelAttackRange && _attackTimer <= 0f)
            {
                _targetBarrel.TakeDamage(AttackDamage);
                _attackTimer = AttackCooldown;
            }
        }
        else
        {
            if (dist <= AttackRange && _attackTimer <= 0f)
            {
                target.TakeDamage(AttackDamage);
                _attackTimer = AttackCooldown;
            }

            // Skip the shot entirely when the line of fire is blocked — see blockedLineOfFire
            // above — so it doesn't waste its cooldown lobbing fireballs into a wall while it
            // repositions for a clear shot.
            if (dist <= ShootRange && _shootTimer <= 0f && !blockedLineOfFire)
            {
                FireProjectile(dir);
                _shootTimer = ShootCooldown;
            }
        }

        if (Multiplayer.HasMultiplayerPeer())
            Rpc(MethodName.SyncEnemyState, GlobalPosition, Rotation, _currentHealth);
    }

    /// <summary>
    /// True if nothing solid sits between here and <paramref name="to"/>. Mask 1 is the same
    /// "blocking" set BarrelWeapon.CanPlaceAt uses — static geometry and barrels — so a barrel
    /// wall counts as blocking a shot exactly the same way an arena wall does.
    /// </summary>
    private bool HasLineOfSight(Vector2 to)
    {
        var space = GetWorld2D()?.DirectSpaceState;
        if (space == null) return true; // no physics context yet — don't block firing/movement
        var query = PhysicsRayQueryParameters2D.Create(GlobalPosition, to);
        query.CollisionMask = 1;
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        return space.IntersectRay(query).Count == 0;
    }

    private void FireProjectile(Vector2 dir)
    {
        var origin = GlobalPosition + dir * 18f;
        SpawnProjectile(origin, dir, cosmetic: false);

        // Demons only run on the host, so its fireball only ever existed there: a client saw
        // nothing at all — no sprite approaching, no warning, just health disappearing when
        // the host resolved the hit. Mirror the shot so everyone can see and dodge it.
        if (NetworkManager.IsNetworked)
            Rpc(MethodName.SpawnProjectileRpc, origin, dir);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    private void SpawnProjectileRpc(Vector2 origin, Vector2 dir) =>
        SpawnProjectile(origin, dir, cosmetic: true);

    private void SpawnProjectile(Vector2 origin, Vector2 dir, bool cosmetic)
    {
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/DemonProjectile.tscn");
        if (scene == null) return;
        var proj = scene.Instantiate<DemonProjectile>();
        proj.Cosmetic = cosmetic; // the host's copy is the one that deals damage
        (_projectileContainer ?? GetParent()).AddChild(proj);
        proj.Init(origin, dir);
    }

    protected override void BuildVisual()
    {
        // Canvas 506x477, character's visual centre (258, 238.5) — taken from the art's ALPHA BOUNDS, not the
        // canvas middle, so the body stays pinned to the node origin where the collision circle
        // and pathing live. Both numbers are derived, never eyeballed: after any art re-export run
        // `python Tools/sprite_metrics.py emit` and paste what it prints.
        const float scale = 0.11333f;
        _visual = new Sprite2D
        {
            Texture  = ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Enemies/demonio.png"),
            Centered = false,
            Scale    = new Vector2(scale, scale),
            Position = new Vector2(-258f * scale, -238.5f * scale),
        };
        AddChild(_visual);

        AddChild(new ColorRect
        {
            Color    = new Color(0.2f, 0.2f, 0.2f),
            Size     = new Vector2(34, 4),
            Position = new Vector2(-17, -31)
        });

        _healthFill = new ColorRect
        {
            Color    = new Color(0.9f, 0.1f, 0.1f),
            Size     = new Vector2(34, 4),
            Position = new Vector2(-17, -31)
        };
        AddChild(_healthFill);
        _healthBarWidth  = 34f;
        _healthBarHeight = 4f;

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 14f } });
    }
}
