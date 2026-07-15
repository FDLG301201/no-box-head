using Godot;

namespace NoBoxHead;

/// <summary>
/// Enemy character. Simulated on the host; state replicated to clients via RPC.
/// Uses direct movement toward the nearest player (no Navigation needed for v1).
/// </summary>
public partial class Enemy : CharacterBody2D
{
    [Export] public float MoveSpeed = 70f;
    [Export] public float MaxHealth = 30f;
    [Export] public float AttackDamage = 10f;
    [Export] public float AttackCooldown = 1.0f;
    [Export] public float AttackRange = 20f;

    public bool IsAlive => _currentHealth > 0f;

    private float _currentHealth;
    private float _attackTimer;
    private ColorRect? _visual;
    private ColorRect? _healthFill;
    private bool _isHost;

    private static readonly Color EnemyColor = new(0.15f, 0.7f, 0.25f); // zombie green

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _currentHealth = MaxHealth;
        _isHost = !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
        BuildPlaceholderVisual();
        AddToGroup("enemies");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isHost || !IsAlive) return;

        var target = GameManager.Instance?.GetNearestPlayer(GlobalPosition);
        if (target == null || !target.IsAlive) return;

        Vector2 dir = (target.GlobalPosition - GlobalPosition).Normalized();
        Velocity = dir * MoveSpeed;

        // Simple separation from other enemies.
        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is Enemy other && other != this && IsInstanceValid(other))
            {
                float dist = GlobalPosition.DistanceTo(other.GlobalPosition);
                if (dist < 24f && dist > 0f)
                    Velocity += (GlobalPosition - other.GlobalPosition).Normalized() * (24f - dist);
            }
        }

        MoveAndSlide();
        Rotation = dir.Angle() + Mathf.Pi / 2f;

        // Attack logic.
        _attackTimer -= (float)delta;
        if (GlobalPosition.DistanceTo(target.GlobalPosition) <= AttackRange && _attackTimer <= 0f)
        {
            target.TakeDamage(AttackDamage);
            _attackTimer = AttackCooldown;
        }

        // Sync to clients (unreliable, frequent).
        if (Multiplayer.HasMultiplayerPeer())
            Rpc(MethodName.SyncEnemyState, GlobalPosition, Rotation, _currentHealth);
    }

    // ── Damage / Death ────────────────────────────────────────────────────────

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
        Modulate = Colors.White;
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
        GameManager.Instance?.OnEnemyKilled();

        // Deferred so signals process before removal.
        CallDeferred(Node.MethodName.QueueFree);
    }

    // ── Network sync ──────────────────────────────────────────────────────────

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SyncEnemyState(Vector2 position, float rotation, float health)
    {
        GlobalPosition = position;
        Rotation = rotation;
        _currentHealth = health;
        UpdateHealthBar();
    }

    // ── Visual ────────────────────────────────────────────────────────────────

    private void BuildPlaceholderVisual()
    {
        _visual = new ColorRect
        {
            Color = EnemyColor,
            Size = new Vector2(22, 22),
            Position = new Vector2(-11, -11)
        };
        AddChild(_visual);

        var bg = new ColorRect
        {
            Color = new Color(0.2f, 0.2f, 0.2f),
            Size = new Vector2(26, 4),
            Position = new Vector2(-13, -18)
        };
        AddChild(bg);

        _healthFill = new ColorRect
        {
            Color = new Color(0.9f, 0.2f, 0.2f),
            Size = new Vector2(26, 4),
            Position = new Vector2(-13, -18)
        };
        AddChild(_healthFill);

        var shape = new CollisionShape2D();
        shape.Shape = new CircleShape2D { Radius = 11f };
        AddChild(shape);
    }

    private void UpdateHealthBar()
    {
        if (_healthFill == null) return;
        _healthFill.Size = new Vector2(26f * (_currentHealth / MaxHealth), 4f);
    }
}
