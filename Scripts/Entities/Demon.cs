using Godot;

namespace NoBoxHead;

/// <summary>
/// Demon enemy. Tougher than zombie, slower, navigates around walls,
/// and fires projectiles. Appears from wave 3. Worth 25 pts on kill.
/// </summary>
public partial class Demon : CharacterBody2D, IDamageable, IKnockbackable
{
    [Export] public float MoveSpeed      = 35f;
    [Export] public float MaxHealth      = 80f;
    [Export] public float AttackDamage   = 15f;
    [Export] public float AttackCooldown = 1.2f;
    [Export] public float AttackRange    = 30f;
    [Export] public float ShootRange     = 250f;
    [Export] public float ShootCooldown  = 2.5f;

    public bool IsAlive => _currentHealth > 0f;

    private float               _currentHealth;
    private float               _attackTimer;
    private float               _shootTimer;
    private Vector2             _knockback;
    private ColorRect?          _visual;
    private ColorRect?          _healthFill;
    private bool                _isHost;
    private Node?               _projectileContainer;
    private NavigationAgent2D?  _navAgent;

    private static readonly Color DemonColor = new(0.85f, 0.1f, 0.1f);

    public void SetProjectileContainer(Node container) => _projectileContainer = container;

    public override void _Ready()
    {
        _currentHealth = MaxHealth;
        _isHost = !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
        BuildVisual();
        AddToGroup("enemies");

        if (_isHost)
        {
            _navAgent = new NavigationAgent2D
            {
                PathDesiredDistance   = 4f,
                TargetDesiredDistance = 20f,
                AvoidanceEnabled      = false,
            };
            AddChild(_navAgent);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isHost || !IsAlive) return;

        var target = GameManager.Instance?.GetNearestPlayer(GlobalPosition);
        if (target == null || !target.IsAlive) return;

        float   dist = GlobalPosition.DistanceTo(target.GlobalPosition);
        Vector2 dir  = (target.GlobalPosition - GlobalPosition).Normalized();

        // Navigate toward player only while outside preferred shoot range.
        if (dist > ShootRange * 0.65f)
        {
            Vector2 navDir = dir;
            if (_navAgent != null)
            {
                _navAgent.TargetPosition = target.GlobalPosition;
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
                    if (d < 24f && d > 0f)
                        Velocity += (GlobalPosition - other.GlobalPosition).Normalized() * (24f - d) * 0.5f;
                }
            }
        }
        else
        {
            Velocity = Vector2.Zero;
        }

        // Apply and decay knockback impulse (always, even while stationary).
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

        Rotation = dir.Angle() + Mathf.Pi / 2f;
        _attackTimer -= (float)delta;
        _shootTimer  -= (float)delta;

        if (dist <= AttackRange && _attackTimer <= 0f)
        {
            target.TakeDamage(AttackDamage);
            _attackTimer = AttackCooldown;
        }

        if (dist <= ShootRange && _shootTimer <= 0f)
        {
            FireProjectile(dir);
            _shootTimer = ShootCooldown;
        }

        if (Multiplayer.HasMultiplayerPeer())
            Rpc(MethodName.SyncState, GlobalPosition, Rotation, _currentHealth);
    }

    private void FireProjectile(Vector2 dir)
    {
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/DemonProjectile.tscn");
        if (scene == null) return;
        var proj = scene.Instantiate<DemonProjectile>();
        (_projectileContainer ?? GetParent()).AddChild(proj);
        proj.Init(GlobalPosition + dir * 18f, dir);
    }

    public void ApplyKnockback(Vector2 impulse) => _knockback += impulse;

    public void TakeDamage(float amount)
    {
        if (!_isHost || !IsAlive) return;
        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        UpdateHealthBar();
        if (Multiplayer.HasMultiplayerPeer())
            Rpc(MethodName.ApplyDamageRpc, _currentHealth);
        FlashDamage();
        if (_currentHealth <= 0f)
        {
            if (Multiplayer.HasMultiplayerPeer()) Rpc(MethodName.DieRpc);
            else DieRpc();
        }
    }

    private async void FlashDamage()
    {
        Modulate = new Color(1f, 0.6f, 0.6f);
        await ToSignal(GetTree().CreateTimer(0.12), SceneTreeTimer.SignalName.Timeout);
        if (IsInstanceValid(this)) Modulate = Colors.White;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    private void ApplyDamageRpc(float newHealth)
    {
        _currentHealth = newHealth;
        UpdateHealthBar();
        FlashDamage();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void DieRpc()
    {
        _currentHealth = 0f;
        if (_visual != null) _visual.Color = Colors.DarkGray;
        SetPhysicsProcess(false);
        ScoreManager.Instance?.RegisterKill(25);
        GameManager.Instance?.OnEnemyKilled();
        DropAmmoPack();
        CallDeferred(Node.MethodName.QueueFree);
    }

    private void DropAmmoPack()
    {
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/AmmoPack.tscn");
        if (scene == null) return;
        var pack = scene.Instantiate<AmmoPack>();
        pack.AmmoAmount     = 6;
        pack.WeaponType     = ScoreManager.Instance?.GetRandomUnlockedAmmoType() ?? "Pistol";
        pack.GlobalPosition = GlobalPosition;
        GetParent()?.AddChild(pack);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SyncState(Vector2 position, float rotation, float health)
    {
        GlobalPosition = position;
        Rotation       = rotation;
        _currentHealth = health;
        UpdateHealthBar();
    }

    private void BuildVisual()
    {
        _visual = new ColorRect
        {
            Color    = DemonColor,
            Size     = new Vector2(28, 28),
            Position = new Vector2(-14, -14)
        };
        AddChild(_visual);

        AddChild(new ColorRect
        {
            Color    = new Color(0.2f, 0.2f, 0.2f),
            Size     = new Vector2(32, 4),
            Position = new Vector2(-16, -24)
        });

        _healthFill = new ColorRect
        {
            Color    = new Color(0.9f, 0.1f, 0.1f),
            Size     = new Vector2(32, 4),
            Position = new Vector2(-16, -24)
        };
        AddChild(_healthFill);

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 14f } });
    }

    private void UpdateHealthBar()
    {
        if (_healthFill == null) return;
        _healthFill.Size = new Vector2(32f * (_currentHealth / MaxHealth), 4f);
    }
}
