using Godot;

namespace NoBoxHead;

/// <summary>
/// Fast, fragile zombie variant. 1.5x a regular zombie's speed but half its health — dies in
/// a couple of hits but closes distance quickly and punishes standing still. Shares Enemy's
/// navigation / stuck-recovery / barrel-breaking behaviour.
/// </summary>
public partial class Sprinter : CharacterBody2D, IDamageable, IKnockbackable
{
    [Export] public float MoveSpeed      = 45f; // 1.5x Enemy's 30
    [Export] public float MaxHealth      = 15f; // half Enemy's 30
    [Export] public float AttackDamage   = 6f;
    [Export] public float AttackCooldown = 0.7f;
    [Export] public float AttackRange    = 26f;

    public bool IsAlive => _currentHealth > 0f;

    private float               _currentHealth;
    private float               _attackTimer;
    private Vector2             _knockback;
    private ColorRect?          _visual;
    private ColorRect?          _healthFill;
    private bool                _isHost;
    private NavigationAgent2D?  _navAgent;

    private Vector2             _prevPosition;
    private float               _stuckTimer;
    private float               _stuckSide = 1f;
    private const float         StuckWindow   = 0.2f; // fast mover — recover from clips quicker
    private const float         StuckMinRatio = 0.2f;

    private Barrel?              _targetBarrel;
    private float                _barrelAttackTimer;
    private const float          BarrelAttackRange = 36f;

    private static readonly Color SprinterColor = new(0.95f, 0.6f, 0.1f);

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
                PathDesiredDistance   = 6f,
                TargetDesiredDistance = 18f,
                AvoidanceEnabled      = false,
                Radius                = 9f,
            };
            AddChild(_navAgent);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isHost || !IsAlive) return;

        var target = GameManager.Instance?.GetNearestPlayer(GlobalPosition);
        if (target == null || !target.IsAlive) return;

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

        Velocity = dir * MoveSpeed;

        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is Node2D other && other != this && IsInstanceValid(other))
            {
                float d = GlobalPosition.DistanceTo(other.GlobalPosition);
                if (d < 20f && d > 0f)
                    Velocity += (GlobalPosition - other.GlobalPosition).Normalized() * (20f - d) * 0.5f;
            }
        }

        if (_knockback.LengthSquared() > 1f)
        {
            Velocity += _knockback;
            _knockback *= 0.7f;
        }
        else
        {
            _knockback = Vector2.Zero;
        }

        MoveAndSlide();

        float movedDist    = GlobalPosition.DistanceTo(_prevPosition);
        float expectedDist = MoveSpeed * (float)delta;
        if (expectedDist > 0f && movedDist < expectedDist * StuckMinRatio)
        {
            _stuckTimer += (float)delta;
            if (_stuckTimer >= StuckWindow)
            {
                Velocity = dir.Rotated(_stuckSide * Mathf.Pi * 0.5f) * MoveSpeed;
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

        Vector2 faceDir = Velocity.LengthSquared() > 1f ? Velocity : dir;
        if (faceDir.LengthSquared() > 0.001f)
        {
            float targetAngle = faceDir.Angle() + Mathf.Pi / 2f;
            Rotation = Mathf.LerpAngle(Rotation, targetAngle, (float)delta * 14f);
        }

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
        await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
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
        if (_visual != null) _visual.Color = new Color(0.3f, 0.3f, 0.3f);
        SetPhysicsProcess(false);
        ScoreManager.Instance?.RegisterKill(8);
        GameManager.Instance?.OnEnemyKilled();
        TryDropAmmo();
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
        _visual = new ColorRect
        {
            Color    = SprinterColor,
            Size     = new Vector2(18, 18),
            Position = new Vector2(-9, -9)
        };
        AddChild(_visual);

        AddChild(new ColorRect
        {
            Color    = new Color(0.2f, 0.2f, 0.2f),
            Size     = new Vector2(22, 4),
            Position = new Vector2(-11, -15)
        });

        _healthFill = new ColorRect
        {
            Color    = new Color(0.9f, 0.2f, 0.2f),
            Size     = new Vector2(22, 4),
            Position = new Vector2(-11, -15)
        };
        AddChild(_healthFill);

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 9f } });
    }

    private void UpdateHealthBar()
    {
        if (_healthFill == null) return;
        _healthFill.Size = new Vector2(22f * (_currentHealth / MaxHealth), 4f);
    }
}
