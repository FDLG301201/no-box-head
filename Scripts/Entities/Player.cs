using Godot;
using System.Linq;

namespace NoBoxHead;

/// <summary>
/// Player character. Each peer is the MultiplayerAuthority of their own Player node.
/// Position/rotation are synced unreliably; damage/death via reliable RPCs.
/// </summary>
public partial class Player : CharacterBody2D
{
    [Export] public float MoveSpeed = 160f;
    [Export] public float MaxHealth = 100f;

    // Set by Arena when spawning.
    public int PlayerIndex { get; set; }

    [Signal] public delegate void HealthChangedEventHandler(float current, float max);

    public float CurrentHealth { get; private set; }
    public bool IsAlive => CurrentHealth > 0f;

    // Visual colours per player slot.
    private static readonly Color[] PlayerColors =
    {
        new(0.2f, 0.4f, 1f),   // blue
        new(1f,   0.3f, 0.3f), // red
        new(0.3f, 0.9f, 0.3f), // green
        new(1f,   0.9f, 0.2f), // yellow
    };

    private ColorRect? _visual;
    private ColorRect? _healthFill;
    private Weapon? _currentWeapon;
    private VirtualJoystick? _moveJoystick;
    private VirtualJoystick? _aimJoystick;
    private bool _isLocalPlayer;

    // ── Godot callbacks ───────────────────────────────────────────────────────

    public override void _Ready()
    {
        CurrentHealth = MaxHealth;
        // IsMultiplayerAuthority() calls GetUniqueId() internally, which errors without a peer.
        // In single-player (no peer), all nodes default to authority 1 — treat the player as local.
        _isLocalPlayer = !Multiplayer.HasMultiplayerPeer() || IsMultiplayerAuthority();

        BuildPlaceholderVisual();
        AddToGroup("players");

        if (_isLocalPlayer)
            GameManager.Instance?.RegisterPlayer(this);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isLocalPlayer || !IsAlive) return;

        Vector2 moveDir = GetMoveInput();
        Velocity = moveDir * MoveSpeed;
        MoveAndSlide();

        UpdateAnimation(moveDir);

        if (_currentWeapon != null)
        {
            Vector2 aimDir = GetAimDirection();
            if (aimDir.LengthSquared() > 0.01f)
            {
                Rotation = aimDir.Angle() + Mathf.Pi / 2f;

                if (ShouldShoot())
                    _currentWeapon.TryShoot(GlobalPosition, aimDir);
            }
        }

        // Unreliable position sync.
        if (Multiplayer.HasMultiplayerPeer())
            Rpc(MethodName.SyncState, GlobalPosition, Rotation);
    }

    // ── Input helpers ─────────────────────────────────────────────────────────

    private Vector2 GetMoveInput()
    {
        if (_moveJoystick?.IsActive == true)
            return _moveJoystick.InputVector;

        return Input.GetVector("move_left", "move_right", "move_up", "move_down");
    }

    private Vector2 GetAimDirection()
    {
        if (_aimJoystick?.IsActive == true)
            return _aimJoystick.InputVector;

        // Auto-aim: nearest enemy.
        var nearest = GetTree().GetNodesInGroup("enemies")
            .OfType<Enemy>()
            .Where(e => IsInstanceValid(e) && e.IsAlive)
            .OrderBy(e => GlobalPosition.DistanceTo(e.GlobalPosition))
            .FirstOrDefault();

        if (nearest != null)
            return (nearest.GlobalPosition - GlobalPosition).Normalized();

        // Fallback: face movement direction.
        return GetMoveInput();
    }

    private bool ShouldShoot()
    {
        if (_aimJoystick?.IsActive == true) return true;
        return Input.IsActionPressed("shoot");
    }

    // ── Visual helpers ────────────────────────────────────────────────────────

    private void BuildPlaceholderVisual()
    {
        // Body rectangle.
        _visual = new ColorRect
        {
            Color = PlayerColors[PlayerIndex % PlayerColors.Length],
            Size = new Vector2(24, 24),
            Position = new Vector2(-12, -12)
        };
        AddChild(_visual);

        // Health bar background.
        var hbBg = new ColorRect
        {
            Color = new Color(0.2f, 0.2f, 0.2f),
            Size = new Vector2(28, 4),
            Position = new Vector2(-14, -20)
        };
        AddChild(hbBg);

        // Health bar fill.
        _healthFill = new ColorRect
        {
            Color = new Color(0.2f, 0.9f, 0.2f),
            Size = new Vector2(28, 4),
            Position = new Vector2(-14, -20)
        };
        AddChild(_healthFill);

        // Collision.
        var shape = new CollisionShape2D();
        var circle = new CircleShape2D { Radius = 12f };
        shape.Shape = circle;
        AddChild(shape);
    }

    private void UpdateAnimation(Vector2 moveDir)
    {
        // Placeholder: tint to show movement.
        if (_visual == null) return;
        if (moveDir.LengthSquared() > 0.01f)
            _visual.Color = _visual.Color with { A = 0.85f };
        else
            _visual.Color = _visual.Color with { A = 1f };
    }

    private void UpdateHealthBar()
    {
        if (_healthFill == null) return;
        _healthFill.Size = new Vector2(28f * (CurrentHealth / MaxHealth), 4f);
    }

    // ── Damage / Death ────────────────────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        if (!_isLocalPlayer) return; // only authority applies damage
        if (!IsAlive) return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        UpdateHealthBar();
        FlashDamage();
        EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);

        if (CurrentHealth <= 0f)
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

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
    private void DieRpc()
    {
        CurrentHealth = 0f;
        if (_visual != null) _visual.Color = new Color(0.3f, 0.3f, 0.3f);
        SetPhysicsProcess(false);
        GameManager.Instance?.OnPlayerDied(this);
    }

    // ── Network sync ──────────────────────────────────────────────────────────

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SyncState(Vector2 position, float rotation)
    {
        GlobalPosition = position;
        Rotation = rotation;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetJoysticks(VirtualJoystick? move, VirtualJoystick? aim)
    {
        _moveJoystick = move;
        _aimJoystick = aim;
    }

    public void SetWeapon(Weapon weapon)
    {
        _currentWeapon = weapon;
        AddChild(weapon);
    }
}
