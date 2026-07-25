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

    private Sprite2D?         _visual;
    private Sprite2D?         _weaponVisual;
    private ColorRect?        _healthFill;
    private Color             _spriteTint = Colors.White;

    // WeaponName → held-weapon sprite. Names that aren't listed (e.g. a future weapon)
    // simply show no sprite rather than erroring.
    private static readonly Dictionary<string, string> WeaponSprites = new()
    {
        { "Pistol",      "res://Assets/Sprites/Weapons/pistol.png"     },
        { "Shotgun",     "res://Assets/Sprites/Weapons/shotgun.png"    },
        { "Machine Gun", "res://Assets/Sprites/Weapons/machinegun.png" },
        { "Knife",       "res://Assets/Sprites/Weapons/knife.png"      },
        { "Grenade",     "res://Assets/Sprites/Weapons/grenade.png"    },
        { "Barrel",      "res://Assets/Sprites/Weapons/barrel.png"     },
    };
    // Where the weapon sits relative to the body when facing right (mirrored when facing left).
    private static readonly Vector2 WeaponOffset = new(11f, 4f);
    private const float WeaponScale = 0.5f;
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
            if (ev.IsActionPressed("switch_weapon"))      SwitchToNextWeapon();
            if (ev.IsActionPressed("switch_weapon_prev")) SwitchToPreviousWeapon();
            if (ev.IsActionPressed("knife"))              ToggleKnife();
        }
        else if (ev is InputEventKey { Echo: false } key)
        {
            // P2 uses direct physical key checks so numpad works regardless of Num Lock / action mapping.
            switch (key.PhysicalKeycode)
            {
                case Key.Kp0: _p2ShootHeld = key.Pressed;                break;
                case Key.Kp1: if (key.Pressed) SwitchToNextWeapon();     break;
                case Key.Kp2: if (key.Pressed) ToggleKnife();            break;
                case Key.Kp3: if (key.Pressed) SwitchToPreviousWeapon(); break;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isLocalPlayer || !IsAlive) return;

        var moveDir = GetMoveInput();
        Velocity = moveDir * MoveSpeed;
        MoveAndSlide();
        ClampToArena();
        UpdateAnimation(moveDir);

        if (_currentWeapon != null)
        {
            var aimDir = GetAimDirection(moveDir);
            if (aimDir.LengthSquared() > 0.01f)
            {
                // Fixed front-facing pose (no rotation frames) — mirror horizontally to hint
                // at aim direction instead of rotating the whole body, same as enemies.
                if (Mathf.Abs(aimDir.X) > 0.05f)
                    FaceDirection(aimDir.X < 0f);

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

    // Hard stop at the arena edges. The perimeter walls already block normal movement, but
    // a clamp guarantees a player can never end up outside the playfield (e.g. pushed out by
    // knockback or a physics tunnel at high speed).
    private void ClampToArena()
    {
        const float radius = 12f; // matches the collision circle below
        GlobalPosition = new Vector2(
            Mathf.Clamp(GlobalPosition.X, radius, ArenaLayouts.ArenaW - radius),
            Mathf.Clamp(GlobalPosition.Y, radius, ArenaLayouts.ArenaH - radius));
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
            UpdateWeaponVisual();
        }
    }

    public void SwitchToNextWeapon()
    {
        if (_weapons.Count <= 1) return;
        _previousWeaponIndex = _currentWeaponIndex;
        _currentWeaponIndex  = (_currentWeaponIndex + 1) % _weapons.Count;
        ActivateWeapon(_currentWeaponIndex);
    }

    public void SwitchToPreviousWeapon()
    {
        if (_weapons.Count <= 1) return;
        _previousWeaponIndex = _currentWeaponIndex;
        _currentWeaponIndex  = (_currentWeaponIndex - 1 + _weapons.Count) % _weapons.Count;
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
        UpdateWeaponVisual();
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

    // ── Healing ───────────────────────────────────────────────────────────────

    /// <summary>Restores health up to MaxHealth. Ignored when dead. Returns false if already full.</summary>
    public bool Heal(float amount)
    {
        if (!IsAlive || CurrentHealth >= MaxHealth) return false;
        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        UpdateHealthBar();
        EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
        FlashHeal();
        return true;
    }

    private async void FlashHeal()
    {
        Modulate = new Color(0.5f, 1f, 0.5f);
        await ToSignal(GetTree().CreateTimer(0.12, false), SceneTreeTimer.SignalName.Timeout);
        if (IsInstanceValid(this)) Modulate = Colors.White;
    }

    // ── Knife swing animation ─────────────────────────────────────────────────

    private void ShowKnifeSwing(Vector2 dir)
    {
        if (!IsInstanceValid(this) || GetParent() == null) return;

        const float halfSpread = 0.44f; // ~25° in radians
        const float range      = 62f;   // keep in sync with Knife.MeleeRange
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
        // Sprite's native canvas is 480x580 with the character's visual center around
        // (239, 301) — offset math below keeps that point pinned to the node's origin
        // (where the collision circle and pathing both live) regardless of scale.
        const float scale = 0.078f;
        _spriteTint = PlayerIndex == 0 ? Colors.White : new Color(1f, 0.55f, 0.55f);
        _visual = new Sprite2D
        {
            Texture  = ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Player/jugador.png"),
            Centered = false,
            Scale    = new Vector2(scale, scale),
            Position = new Vector2(-239.5f * scale, -301.5f * scale),
            Modulate = _spriteTint,
        };
        AddChild(_visual);

        // Held-weapon sprite, drawn on top of the body. Centered so it mirrors cleanly.
        _weaponVisual = new Sprite2D
        {
            Centered = true,
            Scale    = new Vector2(WeaponScale, WeaponScale),
            Position = WeaponOffset,
            ZIndex   = 1,
        };
        AddChild(_weaponVisual);

        // Player identity label — always shown in co-op, useful for distinguishing players.
        if (SettingsManager.Instance?.GameMode == GameMode.LocalCoop)
        {
            var nameLabel = new Label
            {
                Text     = $"P{PlayerIndex + 1}",
                Position = new Vector2(-8, -40),
            };
            nameLabel.AddThemeFontSizeOverride("font_size", 12);
            nameLabel.AddThemeColorOverride("font_color", PlayerColors[PlayerIndex % PlayerColors.Length]);
            AddChild(nameLabel);
        }

        AddChild(new ColorRect
        {
            Color    = new Color(0.2f, 0.2f, 0.2f),
            Size     = new Vector2(32, 4),
            Position = new Vector2(-16, -27)
        });

        _healthFill = new ColorRect
        {
            Color    = new Color(0.2f, 0.9f, 0.2f),
            Size     = new Vector2(32, 4),
            Position = new Vector2(-16, -27)
        };
        AddChild(_healthFill);

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 12f } });
    }

    private void UpdateAnimation(Vector2 moveDir)
    {
        if (_visual == null) return;
        _visual.Modulate = _spriteTint with { A = moveDir.LengthSquared() > 0.01f ? 0.85f : 1f };
    }

    // Mirrors the body and moves the weapon to the correct side so it always reads as held.
    private void FaceDirection(bool faceLeft)
    {
        if (_visual != null) _visual.FlipH = faceLeft;
        if (_weaponVisual != null)
        {
            _weaponVisual.FlipH   = faceLeft;
            _weaponVisual.Position = WeaponOffset with { X = faceLeft ? -WeaponOffset.X : WeaponOffset.X };
        }
    }

    // Swaps the held-weapon texture to match the equipped weapon. Hidden if it has no sprite.
    private void UpdateWeaponVisual()
    {
        if (_weaponVisual == null) return;
        if (_currentWeapon != null && WeaponSprites.TryGetValue(_currentWeapon.WeaponName, out var path))
        {
            _weaponVisual.Texture = ResourceLoader.Load<Texture2D>(path);
            _weaponVisual.Visible = true;
        }
        else
        {
            _weaponVisual.Visible = false;
        }
    }

    private void UpdateHealthBar()
    {
        if (_healthFill == null) return;
        _healthFill.Size = new Vector2(32f * (CurrentHealth / MaxHealth), 4f);
    }

    // ── Revive ────────────────────────────────────────────────────────────────

    public void Revive(Vector2 spawnPosition)
    {
        if (IsAlive) return;
        CurrentHealth  = MaxHealth;
        GlobalPosition = spawnPosition;
        Modulate       = Colors.White;
        if (_visual != null) _visual.Modulate = _spriteTint;
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
        BloodSystem.Instance?.Splatter(GlobalPosition, -_lastAimDir, 0.8f);
        AudioManager.Instance?.Play(AudioManager.PlayerHurt, 0.9f, 0.08f);
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
        BloodSystem.Instance?.Pool(GlobalPosition, 1.2f);
        if (_visual != null) _visual.Modulate = new Color(0.35f, 0.35f, 0.35f);
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
