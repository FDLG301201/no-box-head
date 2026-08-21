using Godot;

namespace NoBoxHead;

/// <summary>
/// Shared behaviour for every enemy type (Enemy, Sprinter, Ogre, Demon): NavigationAgent2D
/// chase/stuck-recovery/barrel-breaking, crowd separation, knockback, the damage RPC quartet,
/// and death handling. Subclasses supply only what differs — stats, visuals, radii, drop
/// tuning and knockback scaling — through the virtual/abstract members below.
///
/// Collision (set per-scene): layer 4, mask 1 — walls and barrels only. Player contact is
/// handled in code by CrowdSeparation instead, because neither collision-layer arrangement
/// gives the behaviour we want: masking the player back lets MoveAndSlide's depenetration
/// bulldoze the enemy along in front of a walking player (measured at 270px of travel with the
/// gap constant to two decimals — the "glued zombie" bug), while making the collision mutual
/// turns a pack into an impassable wall. Contact damage is a distance check (see AttackRange),
/// never a physical collision, so nothing here depends on the two bodies blocking each other.
/// </summary>
public abstract partial class EnemyBase : CharacterBody2D, IDamageable, IKnockbackable
{
    [Export] public float MoveSpeed      = 30f;
    [Export] public float MaxHealth      = 30f;
    [Export] public float AttackDamage   = 10f;
    [Export] public float AttackCooldown = 1.0f;
    // Must be > sum of radii (player=12, enemy=11 = 23) so attack fires while touching.
    [Export] public float AttackRange    = 30f;

    public bool IsAlive => _currentHealth > 0f;

    protected float               _currentHealth;
    protected float               _attackTimer;
    protected Vector2             _knockback;
    protected Sprite2D?           _visual;
    protected ColorRect?          _healthFill;
    protected bool                _isHost;
    protected NavigationAgent2D?  _navAgent;

    // Health-bar fill dimensions, set by each subclass's BuildVisual() so the shared
    // UpdateHealthBar() can scale the fill without knowing the concrete size.
    protected float _healthBarWidth;
    protected float _healthBarHeight;

    // Stuck-recovery state.
    protected Vector2             _prevPosition;
    protected float               _stuckTimer;
    protected float               _stuckSide = 1f;
    // Fraction of expected displacement below which the enemy is considered stuck. Identical
    // across every enemy type — only the window before nudging (StuckWindow) varies.
    protected const float         StuckMinRatio = 0.2f;

    // Barrel-breaking state: when the player is unreachable (blocked off by placed barrels),
    // path to and beat down the nearest one instead of wandering.
    protected Barrel?              _targetBarrel;
    protected float                _barrelAttackTimer;

    // ── Per-type tuning hooks ───────────────────────────────────────────────────

    /// <summary>Seconds of minimal movement before nudging sideways to slip past an obstacle.</summary>
    protected virtual float StuckWindow => 0.25f;

    /// <summary>Distance within which enemies push each other apart.</summary>
    protected virtual float SeparationRadius => 24f;

    /// <summary>Distance within which the barrel-breaking attack fires.</summary>
    protected virtual float BarrelAttackRange => 40f;

    /// <summary>Fraction of knockback impulse retained after MoveAndSlide each frame.</summary>
    protected virtual float KnockbackDecay => 0.7f;

    /// <summary>Fraction of an incoming knockback impulse actually applied.</summary>
    protected virtual float KnockbackScale => 1f;

    protected virtual float NavPathDesiredDistance   => 6f;
    protected virtual float NavTargetDesiredDistance => 20f;
    protected virtual float NavRadius                => 12f;

    protected virtual Color FlashColor    => new Color(1f, 0.3f, 0.3f);
    protected virtual float FlashDuration => 0.12f;

    protected virtual int   ScoreValue        => 10;
    protected virtual float BloodPoolScale    => 1f;
    protected virtual float HealthDropChance  => 0.03f;
    protected virtual Color DeathModulate     => new Color(0.4f, 0.4f, 0.4f);

    protected virtual float AmmoDropChance => 0.3f;
    protected virtual int   AmmoDropAmount => 4;

    /// <summary>Build this enemy's sprite, health bar and collision shape (differs per type).</summary>
    protected abstract void BuildVisual();

    // Drives the walk bob. Lives in _Process, not _PhysicsProcess, because the latter returns
    // early on clients — the animation has to run on every peer, and it derives its speed from
    // observed movement so it works whether the motion came from AI or from a position RPC.
    protected SpriteAnimator? _animator;

    // ── Cryo slow ────────────────────────────────────────────────────────────
    // Applied by directly scaling MoveSpeed, the field every subclass's movement code already
    // reads (including Demon and Boss, which fully replace _PhysicsProcess instead of calling
    // base — scaling the field itself is the only way to reach them without touching their
    // files). _slowFactor is the multiplier CURRENTLY baked into MoveSpeed (1 = not slowed);
    // restoring divides it back out, which stays correct even if something else (Boss's enrage)
    // changed MoveSpeed again while the slow was active.
    private float _slowFactor = 1f;
    private float _slowTimer;
    private ColorRect? _slowOverlay;

    public override void _Process(double delta)
    {
        _animator?.Update((float)delta);

        // Countdown lives here rather than in _PhysicsProcess because Demon/Boss override that
        // wholesale and never call base — _Process is the one method every subclass leaves
        // alone, and it already runs on every peer (see the class doc above).
        if (_isHost && _slowTimer > 0f)
        {
            _slowTimer -= (float)delta;
            if (_slowTimer <= 0f)
            {
                MoveSpeed /= _slowFactor;
                _slowFactor = 1f;
                if (NetworkManager.IsNetworked) Rpc(MethodName.SetSlowVisualRpc, false);
                else SetSlowVisualRpc(false);
            }
        }
    }

    /// <summary>
    /// Cryo Gun's effect: temporarily multiplies MoveSpeed by <paramref name="factor"/>
    /// (e.g. 0.35 for a 65% slow) for <paramref name="duration"/> seconds. Refreshes the
    /// duration on a re-hit rather than stacking the speed multiplier. Host-authoritative, like
    /// every other piece of enemy state — the host is the only copy whose MoveSpeed actually
    /// drives movement, so it's the only one allowed to change it; clients are told to show the
    /// tint via a one-shot RPC (SetSlowVisualRpc) instead of computing it themselves.
    /// </summary>
    public void ApplySlow(float factor, float duration)
    {
        if (!_isHost || !IsAlive) return;
        bool wasSlowed = _slowTimer > 0f;
        _slowTimer = Mathf.Max(_slowTimer, duration);
        if (!Mathf.IsEqualApprox(factor, _slowFactor))
        {
            MoveSpeed = MoveSpeed / _slowFactor * factor;
            _slowFactor = factor;
        }
        if (!wasSlowed)
        {
            if (NetworkManager.IsNetworked) Rpc(MethodName.SetSlowVisualRpc, true);
            else SetSlowVisualRpc(true);
        }
    }

    // A dedicated overlay node rather than tinting Modulate/_visual.Modulate directly: those two
    // are already owned elsewhere (FlashDamage resets the root Modulate to white; Boss.
    // EnterEnragedRpc sets _visual.Modulate permanently) — routing the slow tint through either
    // one would have it clobbered by, or clobber, those effects. A separate translucent overlay
    // sidesteps that entirely and composes with both.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void SetSlowVisualRpc(bool active)
    {
        if (active && _slowOverlay == null)
        {
            _slowOverlay = new ColorRect
            {
                Color    = new Color(0.35f, 0.65f, 1f, 0.42f),
                Size     = new Vector2(28, 28),
                Position = new Vector2(-14, -14),
                ZIndex   = 4,
            };
            AddChild(_slowOverlay);
        }
        if (_slowOverlay != null) _slowOverlay.Visible = active;
    }

    public override void _Ready()
    {
        _currentHealth = MaxHealth;
        _isHost = !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
        BuildVisual();
        if (_visual != null) _animator = new SpriteAnimator(this, _visual, MoveSpeed);
        AddToGroup("enemies");

        if (_isHost)
        {
            _navAgent = new NavigationAgent2D
            {
                PathDesiredDistance   = NavPathDesiredDistance,
                TargetDesiredDistance = NavTargetDesiredDistance,
                AvoidanceEnabled      = false,
                Radius                = NavRadius,
            };
            AddChild(_navAgent);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isHost || !IsAlive) return;

        var target = GameManager.Instance?.GetNearestPlayer(GlobalPosition);
        if (target == null || !target.IsAlive) return;

        // ── Navigation direction ──────────────────────────────────────────────
        // Barrels are carved into the nav mesh, so if one is the only thing standing between
        // this zombie and the player, the target becomes unreachable — path to the nearest
        // barrel instead and break it down (see the attack block below).
        Vector2 dir;
        if (_navAgent != null)
        {
            _navAgent.TargetPosition = target.GlobalPosition;
            bool playerReachable = _navAgent.IsTargetReachable();

            if (!playerReachable)
                _targetBarrel = (_targetBarrel != null && IsInstanceValid(_targetBarrel) && _targetBarrel.IsAlive)
                    ? _targetBarrel
                    : FindNearestBarrel();
            else
                _targetBarrel = null;

            Vector2 navTarget = _targetBarrel?.GlobalPosition ?? target.GlobalPosition;
            if (_targetBarrel != null) _navAgent.TargetPosition = navTarget;

            if (!_navAgent.IsNavigationFinished())
            {
                var nextPos = _navAgent.GetNextPathPosition();
                dir = (nextPos - GlobalPosition).LengthSquared() > 4f
                    ? (nextPos - GlobalPosition).Normalized()
                    : (navTarget - GlobalPosition).Normalized();
            }
            else
            {
                dir = (navTarget - GlobalPosition).Normalized();
            }
        }
        else
        {
            dir = (target.GlobalPosition - GlobalPosition).Normalized();
        }

        // Stop pushing forward once within melee range of the player (chasing a barrel still
        // presses all the way in, since a barrel doesn't fight back). Continuing to steer
        // straight into the player made MoveAndSlide's collision-sliding keep the zombie
        // plastered against the player's hitbox even while it was already landing hits —
        // instead of parking beside them, it read as "stuck". Standing still to attack means
        // it only stays put while the player stays in range; step away and it has to give
        // chase again, coming unstuck as a side effect rather than needing special-casing.
        float distToTarget = GlobalPosition.DistanceTo(target.GlobalPosition);
        bool  inMeleeRange = _targetBarrel == null && distToTarget <= AttackRange;
        Velocity = inMeleeRange ? Vector2.Zero : dir * MoveSpeed;

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

        // Step aside for a player pushing through, so a pack is a crowd to shoulder past
        // rather than a wall (see CrowdSeparation). Reusing AttackRange keeps the distance it
        // holds off at identical to the one it stops at, so a zombie shoved inward by the pack
        // behind it drifts back out to where a lone zombie would have parked anyway.
        Velocity += CrowdSeparation.AwayFromPlayers(this, AttackRange);

        // Apply and decay knockback impulse.
        if (_knockback.LengthSquared() > 1f)
        {
            Velocity += _knockback;
            _knockback *= KnockbackDecay;
        }
        else
        {
            _knockback = Vector2.Zero;
        }

        // Captured before MoveAndSlide can shorten Velocity on contact, so a zombie standing
        // still to attack (Velocity already ~0) never misreads that as "clipped a corner".
        float intendedDist = Velocity.Length() * (float)delta;

        MoveAndSlide();

        // ── Stuck recovery ────────────────────────────────────────────────────
        // If the zombie moved much less than expected (clipped against a corner),
        // after a short delay try nudging sideways to slip past the obstacle.
        float movedDist    = GlobalPosition.DistanceTo(_prevPosition);
        float expectedDist = intendedDist;
        if (expectedDist > 0f && movedDist < expectedDist * StuckMinRatio)
        {
            _stuckTimer += (float)delta;
            if (_stuckTimer >= StuckWindow)
            {
                Velocity = dir.Rotated(_stuckSide * Mathf.Pi * 0.5f) * MoveSpeed;
                MoveAndSlide();
                _stuckTimer = 0f;
                _stuckSide  = -_stuckSide; // alternate left/right each time
            }
        }
        else
        {
            _stuckTimer = 0f;
        }
        _prevPosition = GlobalPosition;

        // The sprite is a fixed front-facing pose (no rotation frames), so just mirror it
        // horizontally to hint at travel direction instead of rotating the whole body.
        Vector2 faceDir = Velocity.LengthSquared() > 1f ? Velocity : dir;
        if (_visual != null && Mathf.Abs(faceDir.X) > 5f)
            _visual.FlipH = faceDir.X < 0f;

        _attackTimer -= (float)delta;
        if (GlobalPosition.DistanceTo(target.GlobalPosition) <= AttackRange && _attackTimer <= 0f)
        {
            target.TakeDamage(AttackDamage);
            _attackTimer = AttackCooldown;
        }

        if (_targetBarrel != null && IsInstanceValid(_targetBarrel) && _targetBarrel.IsAlive)
        {
            _barrelAttackTimer -= (float)delta;
            if (GlobalPosition.DistanceTo(_targetBarrel.GlobalPosition) <= BarrelAttackRange &&
                _barrelAttackTimer <= 0f)
            {
                _targetBarrel.TakeDamage(AttackDamage);
                _barrelAttackTimer = AttackCooldown;
            }
        }

        if (Multiplayer.HasMultiplayerPeer())
            Rpc(MethodName.SyncEnemyState, GlobalPosition, Rotation, _currentHealth);
    }

    protected Barrel? FindNearestBarrel()
    {
        Barrel? nearest = null;
        float   minDist = float.MaxValue;
        foreach (var node in GetTree().GetNodesInGroup("barrels"))
        {
            if (node is not Barrel b || !IsInstanceValid(b) || !b.IsAlive) continue;
            float d = GlobalPosition.DistanceTo(b.GlobalPosition);
            if (d < minDist) { minDist = d; nearest = b; }
        }
        return nearest;
    }

    public void ApplyKnockback(Vector2 impulse) => _knockback += impulse * KnockbackScale;

    public void TakeDamage(float amount)
    {
        if (!IsAlive) return;

        // Enemies are simulated on the host, so only the host may change their health. But a
        // client's bullet hits the client's OWN copy of the enemy, and dropping the hit here
        // is why a joining player could empty a magazine into a zombie for nothing. Forward
        // the request instead; the host stays the sole authority over the health value.
        if (!_isHost)
        {
            if (NetworkManager.IsNetworked)
                RpcId(1, MethodName.RequestDamageRpc, amount);
            return;
        }

        ApplyDamage(amount);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    protected void RequestDamageRpc(float amount)
    {
        if (_isHost) ApplyDamage(amount);
    }

    protected virtual void ApplyDamage(float amount)
    {
        if (!IsAlive) return;
        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        UpdateHealthBar();
        if (NetworkManager.IsNetworked)
            Rpc(MethodName.ApplyDamageVisualRpc, _currentHealth);
        FlashDamage();
        if (_currentHealth <= 0f)
        {
            if (NetworkManager.IsNetworked) Rpc(MethodName.DieRpc);
            else DieRpc();
        }
    }

    protected async void FlashDamage()
    {
        Modulate = FlashColor;
        await ToSignal(GetTree().CreateTimer(FlashDuration), SceneTreeTimer.SignalName.Timeout);
        if (IsInstanceValid(this)) Modulate = Colors.White;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    protected void ApplyDamageVisualRpc(float newHealth)
    {
        _currentHealth = newHealth;
        UpdateHealthBar();
        FlashDamage();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    protected virtual void DieRpc()
    {
        _currentHealth = 0f;
        if (_visual != null) _visual.Modulate = DeathModulate;
        SetPhysicsProcess(false);
        ScoreManager.Instance?.RegisterKill(ScoreValue);
        GameManager.Instance?.OnEnemyKilled();
        BloodSystem.Instance?.Pool(GlobalPosition, BloodPoolScale);
        PlayDeathSound();
        // Deferred, because an enemy usually dies inside a bullet's BodyEntered callback — that
        // is, while the physics server is mid-flush. Adding a pickup there (they enable
        // monitoring) throws "Can't change this state while flushing queries" on every single
        // bullet kill. Queued before QueueFree below, so the node is still alive when it runs.
        CallDeferred(MethodName.SpawnDrops);
        CallDeferred(Node.MethodName.QueueFree);
    }

    private void SpawnDrops()
    {
        DropAmmo();
        HealthPack.TryDrop(GetParent(), GlobalPosition, HealthDropChance);
    }

    protected virtual void PlayDeathSound() =>
        AudioManager.Instance?.Play(AudioManager.EnemyDeath, 0.7f, 0.12f);

    protected virtual void DropAmmo()
    {
        if (GD.Randf() >= AmmoDropChance) return;
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/AmmoPack.tscn");
        if (scene == null) return;
        var pack = scene.Instantiate<AmmoPack>();
        pack.AmmoAmount       = AmmoDropAmount;
        pack.WeaponType       = ScoreManager.Instance?.GetRandomUnlockedAmmoType() ?? "Pistol";
        pack.GlobalPosition   = GlobalPosition;
        GetParent()?.AddChild(pack);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    protected void SyncEnemyState(Vector2 position, float rotation, float health)
    {
        GlobalPosition = position;
        Rotation       = rotation;
        _currentHealth = health;
        UpdateHealthBar();
    }

    protected virtual void UpdateHealthBar()
    {
        if (_healthFill == null) return;
        _healthFill.Size = new Vector2(_healthBarWidth * (_currentHealth / MaxHealth), _healthBarHeight);
    }
}
