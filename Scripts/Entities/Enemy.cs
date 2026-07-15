using Godot;

namespace NoBoxHead;

/// <summary>
/// Zombie enemy. Navigates around walls using NavigationAgent2D.
/// Simulated on host; state replicated via RPC.
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
    private ColorRect?          _visual;
    private ColorRect?          _healthFill;
    private bool                _isHost;
    private NavigationAgent2D?  _navAgent;

    private static readonly Color EnemyColor = new(0.15f, 0.7f, 0.25f);

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
                PathDesiredDistance    = 4f,
                TargetDesiredDistance  = 16f,
                AvoidanceEnabled       = false,
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
            if (!_navAgent.IsNavigationFinished())
            {
                var nextPos = _navAgent.GetNextPathPosition();
                dir = nextPos.DistanceTo(GlobalPosition) > 1f
                    ? (nextPos - GlobalPosition).Normalized()
                    : (target.GlobalPosition - GlobalPosition).Normalized();
            }
            else
            {
                dir = (target.GlobalPosition - GlobalPosition).Normalized();
            }
        }
        else
        {
            dir = (target.GlobalPosition - GlobalPosition).Normalized();
        }

        Velocity = dir * MoveSpeed;

        // Separation from other enemies (works for both Enemy and Demon).
        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is Node2D other && other != this && IsInstanceValid(other))
            {
                float d = GlobalPosition.DistanceTo(other.GlobalPosition);
                if (d < 24f && d > 0f)
                    Velocity += (GlobalPosition - other.GlobalPosition).Normalized() * (24f - d) * 0.5f;
            }
        }

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

        MoveAndSlide();

        // Face the player, not the nav waypoint.
        var toTarget = (target.GlobalPosition - GlobalPosition);
        if (toTarget.LengthSquared() > 0f)
            Rotation = toTarget.Normalized().Angle() + Mathf.Pi / 2f;

        _attackTimer -= (float)delta;
        if (GlobalPosition.DistanceTo(target.GlobalPosition) <= AttackRange && _attackTimer <= 0f)
        {
            target.TakeDamage(AttackDamage);
            _attackTimer = AttackCooldown;
        }

        if (Multiplayer.HasMultiplayerPeer())
            Rpc(MethodName.SyncEnemyState, GlobalPosition, Rotation, _currentHealth);
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
        if (_visual != null) _visual.Color = new Color(0.3f, 0.3f, 0.3f);
        SetPhysicsProcess(false);
        ScoreManager.Instance?.RegisterKill(10);
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
            Color    = EnemyColor,
            Size     = new Vector2(22, 22),
            Position = new Vector2(-11, -11)
        };
        AddChild(_visual);

        AddChild(new ColorRect
        {
            Color    = new Color(0.2f, 0.2f, 0.2f),
            Size     = new Vector2(26, 4),
            Position = new Vector2(-13, -18)
        });

        _healthFill = new ColorRect
        {
            Color    = new Color(0.9f, 0.2f, 0.2f),
            Size     = new Vector2(26, 4),
            Position = new Vector2(-13, -18)
        };
        AddChild(_healthFill);

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 11f } });
    }

    private void UpdateHealthBar()
    {
        if (_healthFill == null) return;
        _healthFill.Size = new Vector2(26f * (_currentHealth / MaxHealth), 4f);
    }
}
