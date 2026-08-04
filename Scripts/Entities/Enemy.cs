using Godot;

namespace NoBoxHead;

/// <summary>
/// Zombie enemy. Navigates around walls using NavigationAgent2D.
/// Simulated on host; state replicated via RPC.
///
/// Collision (set in Enemy.tscn): layer 4, mask 1 — walls and barrels only. Player contact is
/// handled in code by CrowdSeparation instead, because neither collision-layer arrangement
/// gives the behaviour we want: masking the player back lets MoveAndSlide's depenetration
/// bulldoze the enemy along in front of a walking player (measured at 270px of travel with the
/// gap constant to two decimals — the "glued zombie" bug), while making the collision mutual
/// turns a pack into an impassable wall. Contact damage is a distance check (see AttackRange),
/// never a physical collision, so nothing here depends on the two bodies blocking each other.
/// </summary>
public partial class Enemy : CharacterBody2D, IDamageable, IKnockbackable
{
    [Export] public float MoveSpeed      = 30f;
    [Export] public float MaxHealth      = 30f;
    [Export] public float AttackDamage   = 10f;
    [Export] public float AttackCooldown = 1.0f;
    // Must be > sum of radii (player=12, enemy=11 = 23) so attack fires while touching.
    [Export] public float AttackRange    = 30f;

    public bool IsAlive => _currentHealth > 0f;

    private float               _currentHealth;
    private float               _attackTimer;
    private Vector2             _knockback;
    private Sprite2D?           _visual;
    private ColorRect?          _healthFill;
    private bool                _isHost;
    private NavigationAgent2D?  _navAgent;

    // Stuck-recovery state.
    private Vector2             _prevPosition;
    private float               _stuckTimer;
    private float               _stuckSide = 1f;
    private const float         StuckWindow = 0.25f; // seconds of minimal movement before nudging
    private const float         StuckMinRatio = 0.2f;  // fraction of expected displacement

    // Barrel-breaking state: when the player is unreachable (blocked off by placed barrels),
    // path to and beat down the nearest one instead of wandering.
    private Barrel?              _targetBarrel;
    private float                _barrelAttackTimer;
    private const float          BarrelAttackRange = 40f;

    public override void _Ready()
    {
        _currentHealth = MaxHealth;
        _isHost = !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
        BuildPlaceholderVisual();
        AddToGroup("enemies");

        if (_isHost)
        {
            _navAgent = new NavigationAgent2D
            {
                PathDesiredDistance   = 6f,   // follow waypoints tightly for closer cornering
                TargetDesiredDistance = 20f,
                AvoidanceEnabled      = false,
                Radius                = 12f,
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
                if (d < 24f && d > 0f)
                    Velocity += (GlobalPosition - other.GlobalPosition).Normalized() * (24f - d) * 0.5f;
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
            _knockback *= 0.7f;
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

    private Barrel? FindNearestBarrel()
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

    public void ApplyKnockback(Vector2 impulse) => _knockback += impulse;

    public void TakeDamage(float amount)
    {
        if (!_isHost || !IsAlive) return;
        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        UpdateHealthBar();
        if (Multiplayer.HasMultiplayerPeer())
            Rpc(MethodName.ApplyDamageVisualRpc, _currentHealth);
        FlashDamage();
        if (_currentHealth <= 0f)
        {
            if (Multiplayer.HasMultiplayerPeer()) Rpc(MethodName.DieRpc);
            else DieRpc();
        }
    }

    private async void FlashDamage()
    {
        Modulate = new Color(1f, 0.3f, 0.3f);
        await ToSignal(GetTree().CreateTimer(0.12), SceneTreeTimer.SignalName.Timeout);
        if (IsInstanceValid(this)) Modulate = Colors.White;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    private void ApplyDamageVisualRpc(float newHealth)
    {
        _currentHealth = newHealth;
        UpdateHealthBar();
        FlashDamage();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void DieRpc()
    {
        _currentHealth = 0f;
        if (_visual != null) _visual.Modulate = new Color(0.4f, 0.4f, 0.4f);
        SetPhysicsProcess(false);
        ScoreManager.Instance?.RegisterKill(10);
        GameManager.Instance?.OnEnemyKilled();
        BloodSystem.Instance?.Pool(GlobalPosition);
        AudioManager.Instance?.Play(AudioManager.EnemyDeath, 0.7f, 0.12f);
        TryDropAmmo();
        HealthPack.TryDrop(GetParent(), GlobalPosition, 0.03f);
        CallDeferred(Node.MethodName.QueueFree);
    }

    private void TryDropAmmo()
    {
        if (GD.Randf() >= 0.3f) return;
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/AmmoPack.tscn");
        if (scene == null) return;
        var pack = scene.Instantiate<AmmoPack>();
        pack.AmmoAmount       = 4;
        pack.WeaponType       = ScoreManager.Instance?.GetRandomUnlockedAmmoType() ?? "Pistol";
        pack.GlobalPosition   = GlobalPosition;
        GetParent()?.AddChild(pack);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SyncEnemyState(Vector2 position, float rotation, float health)
    {
        GlobalPosition = position;
        Rotation       = rotation;
        _currentHealth = health;
        UpdateHealthBar();
    }

    private void BuildPlaceholderVisual()
    {
        // Sprite's native canvas is 480x580 with the character's visual center around
        // (239, 301) — offset math below keeps that point pinned to the node's origin
        // (where the collision circle and pathing both live) regardless of scale.
        const float scale = 0.075f;
        _visual = new Sprite2D
        {
            Texture  = ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Enemies/zombie.png"),
            Centered = false,
            Scale    = new Vector2(scale, scale),
            Position = new Vector2(-239f * scale, -301.5f * scale),
        };
        AddChild(_visual);

        AddChild(new ColorRect
        {
            Color    = new Color(0.2f, 0.2f, 0.2f),
            Size     = new Vector2(30, 4),
            Position = new Vector2(-15, -29)
        });

        _healthFill = new ColorRect
        {
            Color    = new Color(0.9f, 0.2f, 0.2f),
            Size     = new Vector2(30, 4),
            Position = new Vector2(-15, -29)
        };
        AddChild(_healthFill);

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 11f } });
    }

    private void UpdateHealthBar()
    {
        if (_healthFill == null) return;
        _healthFill.Size = new Vector2(30f * (_currentHealth / MaxHealth), 4f);
    }
}
