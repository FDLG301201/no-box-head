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
        { "Railgun",     "res://Assets/Sprites/Weapons/railgun.png"    },
        { "Flak Shotgun","res://Assets/Sprites/Weapons/flakshotgun.png"},
        { "Chainsaw",    "res://Assets/Sprites/Weapons/chainsaw.png"   },
        { "Rocket Launcher", "res://Assets/Sprites/Weapons/rocketlauncher.png" },
        { "Proximity Mine",  "res://Assets/Sprites/Weapons/mine.png"           },
        { "Flamethrower",    "res://Assets/Sprites/Weapons/flamethrower.png"   },
        { "Cryo Gun",        "res://Assets/Sprites/Weapons/cryogun.png"        },
        { "Turret",          "res://Assets/Sprites/Weapons/turret.png"         },
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
    private SpriteAnimator?   _animator;
    private Vector2           _lastAimDir = Vector2.Up;
    // Held state of the on-screen fire button (touch builds only).
    private bool              _touchFireHeld;

    /// <summary>Called by the mobile HUD's fire button on press/release.</summary>
    public void SetTouchFireHeld(bool held) => _touchFireHeld = held;

    private const float AutoAimRange   = 700f;

    public override void _Ready()
    {
        CurrentHealth = MaxHealth;
        _isLocalPlayer = !NetworkManager.IsNetworked || IsMultiplayerAuthority();
        BuildPlaceholderVisual();
        // Only the body bobs; the held weapon stays put so it keeps reading as gripped rather
        // than floating alongside a moving hand.
        if (_visual != null) _animator = new SpriteAnimator(this, _visual, MoveSpeed);
        AddToGroup("players");
        // Register every player, not just the one this peer drives. Enemies are simulated on
        // the host and pick their target with GameManager.GetNearestPlayer, so registering
        // only local players left the host's list holding nothing but the host's own
        // character — every zombie in the game ignored the clients and converged on the host.
        GameManager.Instance?.RegisterPlayer(this);
    }

    /// <summary>
    /// True only for the second player of a LOCAL co-op game, who shares one keyboard with
    /// player one and therefore needs the "_p2" half of the bindings (arrows + numpad).
    ///
    /// Online, the joining player is also index 1 but sits at their OWN keyboard, so keying
    /// off the index alone forced a desktop client to play on the arrow keys and the numpad
    /// while WASD and space did nothing.
    /// </summary>
    private bool UsesSecondaryBindings =>
        PlayerIndex != 0 && SettingsManager.Instance?.GameMode == GameMode.LocalCoop;

    // Returns the action name scoped to this player's controls.
    private string A(string action) => UsesSecondaryBindings ? action + "_p2" : action;

    public override void _Input(InputEvent ev)
    {
        if (!_isLocalPlayer) return;
        // A() resolves to the "_p2" action variants for player two of a local co-op game (see
        // UsesSecondaryBindings) — those default to arrows + numpad, defined in the input map
        // with physical keycodes, so this works regardless of Num Lock the same way the old
        // direct-physical-key check did, but through SettingsManager's bindings so it's
        // rebindable from the Settings screen like player one's controls.
        if (ev.IsActionPressed(A("switch_weapon")))      SwitchToNextWeapon();
        if (ev.IsActionPressed(A("switch_weapon_prev"))) SwitchToPreviousWeapon();
        if (ev.IsActionPressed(A("knife")))               ToggleKnife();
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
                    // Auto-switch to knife when a ranged weapon runs dry. Keyed on InfiniteAmmo
                    // rather than "is Knife": the Chainsaw is also an ∞-ammo melee weapon, and
                    // the old type check swapped it out the instant the player tried to use it.
                    if (!_currentWeapon.InfiniteAmmo &&
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
        // Mouse aim needs a single cursor: unusable in local co-op, and there is no cursor
        // at all on touch devices. Fall back to movement aiming in both cases.
        if (mode == AimMode.Mouse &&
            (SettingsManager.Instance?.GameMode == GameMode.LocalCoop || Platform.IsMobile))
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
        // Dragging the aim stick fires (twin-stick), and the fire button covers the aim modes
        // that don't need the stick at all (Movement / Auto-Aim) — without it those modes are
        // unusable on touch, since there'd be no way to shoot without overriding the aim.
        if (_aimJoystick?.IsActive == true) return true;
        if (_touchFireHeld) return true;
        return Input.IsActionPressed(A("shoot"));
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

    public void ToggleKnife()
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
        // Canvas 501x453, character's visual centre (258, 226.5) — taken from the art's ALPHA BOUNDS, not the
        // canvas middle, so the body stays pinned to the node origin where the collision circle
        // and pathing live. Both numbers are derived, never eyeballed: after any art re-export run
        // `python Tools/sprite_metrics.py emit` and paste what it prints.
        const float scale = 0.10400f;
        // Each slot gets its own skin (offset from the chosen one), so players stay
        // distinguishable without tinting the artwork.
        var skin = PlayerSkins.ForPlayer(PlayerIndex);
        _spriteTint = Colors.White;
        _visual = new Sprite2D
        {
            Texture  = ResourceLoader.Load<Texture2D>(skin.Path),
            Centered = false,
            Scale    = new Vector2(scale, scale),
            Position = new Vector2(-258f * scale, -226.5f * scale),
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

    // In _Process rather than _PhysicsProcess so a remote player — whose position arrives by
    // RPC and never runs the movement code here — still animates on this machine.
    public override void _Process(double delta) => _animator?.Update((float)delta);

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
        GameManager.Instance?.RegisterPlayer(this); // targetable again, on every peer
    }

    // ── Damage / Death ────────────────────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        if (!IsAlive) return;

        // Enemies only ever run on the host, so the host is who calls this — including on the
        // player nodes it does not own. Returning early there meant a client could stand in a
        // horde and never lose a point of health. Hand the hit to whoever owns that player, so
        // a peer still remains the only authority over its own health.
        if (!_isLocalPlayer)
        {
            if (NetworkManager.IsNetworked)
                RpcId(GetMultiplayerAuthority(), MethodName.ApplyDamageRpc, amount);
            return;
        }

        ApplyDamage(amount);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    private void ApplyDamageRpc(float amount) => ApplyDamage(amount);

    private void ApplyDamage(float amount)
    {
        if (!IsAlive) return;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        UpdateHealthBar();
        FlashDamage();
        BloodSystem.Instance?.Splatter(GlobalPosition, -_lastAimDir, 0.8f);
        AudioManager.Instance?.Play(AudioManager.PlayerHurt, 0.9f, 0.08f);
        EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);

        // Health is owned locally but drawn everywhere: without this the other peers keep
        // showing a full bar over a player who is nearly dead, and their copy of IsAlive stays
        // true, which is also what enemies test before picking a target.
        if (NetworkManager.IsNetworked) Rpc(MethodName.SyncHealthRpc, CurrentHealth);

        if (CurrentHealth <= 0f)
        {
            if (NetworkManager.IsNetworked) Rpc(MethodName.DieRpc);
            else DieRpc();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    private void SyncHealthRpc(float health)
    {
        CurrentHealth = health;
        UpdateHealthBar();
        FlashDamage();
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

    // Aim scheme (stick vs auto-aim + fire button) can change live via the pause menu on
    // mobile, so it's swappable independently of the move stick.
    public void SetAimJoystick(VirtualJoystick? aim) => _aimJoystick = aim;
}
