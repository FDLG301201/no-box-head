using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

public partial class Player : CharacterBody2D
{
    [Export] public float MoveSpeed = 160f;
    [Export] public float MaxHealth = 100f;

    public int PlayerIndex { get; set; }

    [Signal] public delegate void HealthChangedEventHandler(float current, float max);
    [Signal] public delegate void WeaponChangedEventHandler(string weaponName);
    [Signal] public delegate void AmmoChangedEventHandler(int current, int reserve);
    [Signal] public delegate void ReloadingEventHandler(bool isReloading);
    public float CurrentHealth { get; private set; }
    public bool  IsAlive       => CurrentHealth > 0f;
    public int   WeaponCount   => _weapons.Count;

    private static readonly Color[] PlayerColors =
    {
        new(0.2f, 0.4f, 1f),
        new(1f,   0.3f, 0.3f),
        new(0.3f, 0.9f, 0.3f),
        new(1f,   0.9f, 0.2f),
    };

    private ColorRect?        _visual;
    private ColorRect?        _healthFill;
    private readonly List<Weapon> _weapons = new();
    private int               _currentWeaponIndex;
    private int               _previousWeaponIndex;
    private Weapon?           _currentWeapon;
    private VirtualJoystick?  _moveJoystick;
    private VirtualJoystick?  _aimJoystick;
    private bool              _isLocalPlayer;
    private Vector2           _lastAimDir = Vector2.Up;
    // Tracks KP_0 held state for P2 (physical key check, Num Lock independent).
    private bool              _p2ShootHeld;

    private const float RotationSpeed  = 14f;
    private const float AutoAimRange   = 700f;

    public override void _Ready()
    {
        CurrentHealth = MaxHealth;
        _isLocalPlayer = !Multiplayer.HasMultiplayerPeer() || IsMultiplayerAuthority();
        BuildPlaceholderVisual();
        AddToGroup("players");
        if (_isLocalPlayer)
            GameManager.Instance?.RegisterPlayer(this);
    }

    // Returns the action name scoped to this player's index.
    private string A(string action) => PlayerIndex == 0 ? action : action + "_p2";

    public override void _Input(InputEvent ev)
    {
        if (!_isLocalPlayer) return;
        if (PlayerIndex == 0)
        {
            if (ev.IsActionPressed("switch_weapon")) SwitchToNextWeapon();
            if (ev.IsActionPressed("knife"))         ToggleKnife();
        }
        else if (ev is InputEventKey { Echo: false } key)
        {
            // P2 uses direct physical key checks so numpad works regardless of Num Lock / action mapping.
            switch (key.PhysicalKeycode)
            {
                case Key.Kp0: _p2ShootHeld = key.Pressed;            break;
                case Key.Kp1: if (key.Pressed) SwitchToNextWeapon(); break;
                case Key.Kp2: if (key.Pressed) ToggleKnife();        break;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isLocalPlayer || !IsAlive) return;

        var moveDir = GetMoveInput();
        Velocity = moveDir * MoveSpeed;
        MoveAndSlide();
        UpdateAnimation(moveDir);

        if (_currentWeapon != null)
        {
            var aimDir = GetAimDirection(moveDir);
            if (aimDir.LengthSquared() > 0.01f)
            {
                float targetAngle = aimDir.Angle() + Mathf.Pi / 2f;
                Rotation = Mathf.LerpAngle(Rotation, targetAngle, (float)delta * RotationSpeed);

                if (ShouldShoot())
                {
                    // Auto-switch to knife when ranged weapon is empty.
                    if (!(_currentWeapon is Knife) &&
                        _currentWeapon.CurrentAmmo <= 0 &&
                        _currentWeapon.ReserveAmmo <= 0)
                    {
                        ToggleKnife();
                    }
                    else
                    {
                        _currentWeapon.TryShoot(GlobalPosition, aimDir);
                    }
                }
            }
        }

        if (Multiplayer.HasMultiplayerPeer())
            Rpc(MethodName.SyncState, GlobalPosition, Rotation);
    }

    // ── Input helpers ─────────────────────────────────────────────────────────

    private Vector2 GetMoveInput()
    {
        if (_moveJoystick?.IsActive == true) return _moveJoystick.InputVector;
        return Input.GetVector(A("move_left"), A("move_right"), A("move_up"), A("move_down"));
    }

    private Vector2 GetAimDirection(Vector2 moveInput)
    {
        if (_aimJoystick?.IsActive == true) return _aimJoystick.InputVector;

        var mode = SettingsManager.Instance?.AimMode ?? AimMode.Movement;
        // Mouse aim requires a single cursor — not usable in local co-op.
        if (SettingsManager.Instance?.GameMode == GameMode.LocalCoop && mode == AimMode.Mouse)
            mode = AimMode.Movement;
        Vector2 aim;

        switch (mode)
        {
            case AimMode.Mouse:
                var toMouse = GetGlobalMousePosition() - GlobalPosition;
                aim = toMouse.LengthSquared() > 1f ? toMouse.Normalized() : _lastAimDir;
                break;

            case AimMode.AutoAim:
                Node2D? nearest = null;
                float   minDist = AutoAimRange;
                foreach (var node in GetTree().GetNodesInGroup("enemies"))
                {
                    if (node is IDamageable d && d.IsAlive && node is Node2D n2d)
                    {
                        float dist = GlobalPosition.DistanceTo(n2d.GlobalPosition);
                        if (dist < minDist) { minDist = dist; nearest = n2d; }
                    }
                }
                aim = nearest != null
                    ? (nearest.GlobalPosition - GlobalPosition).Normalized()
                    : _lastAimDir;
                break;

            default: // AimMode.Movement
                aim = moveInput.LengthSquared() > 0.01f ? moveInput.Normalized() : _lastAimDir;
                break;
        }

        _lastAimDir = aim;
        return aim;
    }

    private bool ShouldShoot()
    {
        if (_aimJoystick?.IsActive == true) return true;
        if (PlayerIndex == 0) return Input.IsActionPressed("shoot");
        return _p2ShootHeld;
    }

    // ── Weapon management ─────────────────────────────────────────────────────

    public void AddWeapon(Weapon weapon)
    {
        weapon.Name = $"Weapon{_weapons.Count}";
        _weapons.Add(weapon);
        AddChild(weapon);

        weapon.AmmoChanged += (cur, res) =>
        {
            if (weapon == _currentWeapon) EmitSignal(SignalName.AmmoChanged, cur, res);
        };
        weapon.Reloading += rel =>
        {
            if (weapon == _currentWeapon) EmitSignal(SignalName.Reloading, rel);
        };

        if (weapon is Knife knife)
            knife.OnAttack = ShowKnifeSwing;

        if (_weapons.Count == 1)
        {
            _currentWeaponIndex = 0;
            _currentWeapon      = weapon;
        }
    }

    public void SwitchToNextWeapon()
    {
        if (_weapons.Count <= 1) return;
        _previousWeaponIndex = _currentWeaponIndex;
        _currentWeaponIndex  = (_currentWeaponIndex + 1) % _weapons.Count;
        ActivateWeapon(_currentWeaponIndex);
    }

    private void ToggleKnife()
    {
        int knifeIdx = _weapons.FindIndex(w => w is Knife);
        if (knifeIdx < 0) return;

        if (_currentWeaponIndex == knifeIdx)
        {
            // Already holding knife → switch back.
            ActivateWeapon(_previousWeaponIndex);
        }
        else
        {
            _previousWeaponIndex = _currentWeaponIndex;
            ActivateWeapon(knifeIdx);
        }
    }

    private void ActivateWeapon(int index)
    {
        _currentWeaponIndex = index;
        _currentWeapon      = _weapons[_currentWeaponIndex];
        EmitSignal(SignalName.WeaponChanged, _currentWeapon.WeaponName);
        EmitSignal(SignalName.AmmoChanged,   _currentWeapon.CurrentAmmo, _currentWeapon.ReserveAmmo);
        EmitSignal(SignalName.Reloading,     _currentWeapon.IsReloading);
    }

    // ── Ammo pickup ───────────────────────────────────────────────────────────

    public void AddAmmo(int amount, string weaponType = "")
    {
        Weapon? target;
        if (weaponType.Length > 0)
        {
            // Route to the weapon whose name matches (e.g. "Pistol", "Shotgun", "Machine Gun").
            target = _weapons.Find(w => !(w is Knife) && w.WeaponName == weaponType);
        }
        else if (_currentWeapon is Knife)
        {
            // Knife active: fall back to first available ranged weapon.
            target = _weapons.Find(w => !(w is Knife));
        }
        else
        {
            target = _currentWeapon;
        }
        target?.AddReserveAmmo(amount);
    }

    // ── Knife swing animation ─────────────────────────────────────────────────

    private void ShowKnifeSwing(Vector2 dir)
    {
        if (!IsInstanceValid(this) || GetParent() == null) return;

        const float halfSpread = 0.44f; // ~25° in radians
        const float range      = 54f;
        const int   segments   = 6;

        float baseAngle = dir.Angle();
        var   pts       = new Vector2[segments + 2];
        pts[0] = Vector2.Zero;
        for (int i = 0; i <= segments; i++)
        {
            float t     = (float)i / segments;
            float angle = baseAngle - halfSpread + t * (halfSpread * 2f);
            pts[i + 1]  = Vector2.FromAngle(angle) * range;
        }

        var fan = new Polygon2D
        {
            Polygon        = pts,
            Color          = new Color(0.85f, 0.95f, 1f, 0.72f),
            GlobalPosition = GlobalPosition,
            ZIndex         = 5,
        };
        GetParent().AddChild(fan);

        var tween = fan.CreateTween();
        tween.TweenProperty(fan, "modulate:a", 0f, 0.15f);
        tween.TweenCallback(Godot.Callable.From(fan.QueueFree));
    }

    // ── Visual helpers ────────────────────────────────────────────────────────

    private void BuildPlaceholderVisual()
    {
        _visual = new ColorRect
        {
            Color    = PlayerColors[PlayerIndex % PlayerColors.Length],
            Size     = new Vector2(24, 24),
            Position = new Vector2(-12, -12)
        };
        AddChild(_visual);

        // Player identity label — always shown in co-op, useful for distinguishing players.
        if (SettingsManager.Instance?.GameMode == GameMode.LocalCoop)
        {
            var nameLabel = new Label
            {
                Text     = $"P{PlayerIndex + 1}",
                Position = new Vector2(-8, -34),
            };
            nameLabel.AddThemeFontSizeOverride("font_size", 12);
            nameLabel.AddThemeColorOverride("font_color", PlayerColors[PlayerIndex % PlayerColors.Length]);
            AddChild(nameLabel);
        }

        AddChild(new ColorRect
        {
            Color    = new Color(0.2f, 0.2f, 0.2f),
            Size     = new Vector2(28, 4),
            Position = new Vector2(-14, -20)
        });

        _healthFill = new ColorRect
        {
            Color    = new Color(0.2f, 0.9f, 0.2f),
            Size     = new Vector2(28, 4),
            Position = new Vector2(-14, -20)
        };
        AddChild(_healthFill);

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 12f } });
    }

    private void UpdateAnimation(Vector2 moveDir)
    {
        if (_visual == null) return;
        _visual.Color = _visual.Color with { A = moveDir.LengthSquared() > 0.01f ? 0.85f : 1f };
    }

    private void UpdateHealthBar()
    {
        if (_healthFill == null) return;
        _healthFill.Size = new Vector2(28f * (CurrentHealth / MaxHealth), 4f);
    }

    // ── Revive ────────────────────────────────────────────────────────────────

    public void Revive(Vector2 spawnPosition)
    {
        if (IsAlive) return;
        CurrentHealth  = MaxHealth;
        GlobalPosition = spawnPosition;
        Modulate       = Colors.White;
        if (_visual != null) _visual.Color = PlayerColors[PlayerIndex % PlayerColors.Length];
        UpdateHealthBar();
        SetPhysicsProcess(true);
        EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
        if (_isLocalPlayer) GameManager.Instance?.RegisterPlayer(this);
    }

    // ── Damage / Death ────────────────────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        if (!_isLocalPlayer || !IsAlive) return;
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
        if (IsInstanceValid(this)) Modulate = Colors.White;
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
        Rotation       = rotation;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetJoysticks(VirtualJoystick? move, VirtualJoystick? aim)
    {
        _moveJoystick = move;
        _aimJoystick  = aim;
    }
}
