using Godot;

namespace NoBoxHead;

public partial class HUD : CanvasLayer
{
    private Label?     _waveLabel;
    private Label?     _enemiesLabel;
    private Label?     _ammoLabel;
    private Label?     _reloadLabel;
    private Label?     _scoreLabel;
    private Label?     _multiplierLabel;
    private Label?     _unlockLabel;
    private Label?     _weaponLabel;
    private ColorRect? _healthFill;
    private Control?   _joystickLayer;

    private ColorRect? _pauseOverlay;
    private ColorRect? _gameOverOverlay;
    private Label?     _goScoreLabel;
    private Label?     _goWaveLabel;

    public bool IsGameOver { get; private set; }

    public System.Action? SwitchWeaponCallback { get; set; }
    public System.Action? PauseCallback        { get; set; }

    private float _maxHealth = 100f;

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev.IsActionPressed("pause") && !IsGameOver)
        {
            PauseCallback?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Ready()
    {
        Layer       = 10;
        ProcessMode = ProcessModeEnum.Always; // respond while tree is paused

        BuildHUD();
        BuildPauseMenu();
        BuildGameOverScreen();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.WaveStarted             += OnWaveStarted;
            GameManager.Instance.EnemiesRemainingChanged += OnEnemiesChanged;
            GameManager.Instance.WaveCompleted           += w =>
            {
                if (_waveLabel != null) _waveLabel.Text = $"Wave {w} complete!";
            };
        }

        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.ScoreChanged   += OnScoreChanged;
            ScoreManager.Instance.WeaponUnlocked += OnWeaponUnlocked;
        }
    }

    // ── HUD elements ──────────────────────────────────────────────────────────

    private void BuildHUD()
    {
        AddChild(MakeRect(new Color(0.15f, 0.15f, 0.15f), new Vector2(200, 16), new Vector2(10, 10)));
        _healthFill = MakeRect(new Color(0.2f, 0.85f, 0.2f), new Vector2(200, 16), new Vector2(10, 10));
        AddChild(_healthFill);

        var hpLabel = new Label { Text = "HP", Position = new Vector2(10, 8) };
        hpLabel.AddThemeFontSizeOverride("font_size", 11);
        AddChild(hpLabel);

        _ammoLabel = new Label { Text = "12 | 12", Position = new Vector2(10, 34) };
        _ammoLabel.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_ammoLabel);

        _reloadLabel = new Label
        {
            Text     = "RELOADING...",
            Position = new Vector2(10, 58),
            Visible  = false,
        };
        _reloadLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.2f));
        _reloadLabel.AddThemeFontSizeOverride("font_size", 16);
        AddChild(_reloadLabel);

        _weaponLabel = new Label { Text = "Pistol", Position = new Vector2(10, 76) };
        _weaponLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        _weaponLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_weaponLabel);

        var switchBtn = new Button
        {
            Text     = "[Q] Switch",
            Position = new Vector2(10, 96),
            Size     = new Vector2(120, 28),
        };
        switchBtn.Pressed += () => SwitchWeaponCallback?.Invoke();
        AddChild(switchBtn);

        _waveLabel = new Label
        {
            Text                = "Wave 1",
            AnchorLeft          = 0.5f, AnchorRight = 0.5f,
            Position            = new Vector2(-60, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _waveLabel.AddThemeFontSizeOverride("font_size", 22);
        AddChild(_waveLabel);

        _enemiesLabel = new Label
        {
            Text       = "Enemies: 0",
            AnchorLeft = 1f, AnchorRight = 1f,
            Position   = new Vector2(-180, 10),
        };
        _enemiesLabel.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_enemiesLabel);

        _scoreLabel = new Label
        {
            Text       = "Score: 0",
            AnchorLeft = 1f, AnchorRight = 1f,
            Position   = new Vector2(-180, 36),
        };
        _scoreLabel.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_scoreLabel);

        _multiplierLabel = new Label
        {
            Text       = "x1.0",
            AnchorLeft = 1f, AnchorRight = 1f,
            Position   = new Vector2(-180, 60),
            Visible    = false,
        };
        _multiplierLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.1f));
        _multiplierLabel.AddThemeFontSizeOverride("font_size", 20);
        AddChild(_multiplierLabel);

        _unlockLabel = new Label
        {
            Text                = "",
            AnchorLeft          = 0.5f, AnchorRight  = 0.5f,
            AnchorTop           = 0.4f, AnchorBottom = 0.4f,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible             = false,
        };
        _unlockLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.4f));
        _unlockLabel.AddThemeFontSizeOverride("font_size", 26);
        AddChild(_unlockLabel);

        _joystickLayer = new Control
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_joystickLayer);
    }

    // ── Pause menu ────────────────────────────────────────────────────────────

    private void BuildPauseMenu()
    {
        _pauseOverlay = new ColorRect
        {
            AnchorRight  = 1f, AnchorBottom = 1f,
            Color        = new Color(0f, 0f, 0f, 0.65f),
            Visible      = false,
        };

        var panel = new PanelContainer
        {
            AnchorLeft     = 0.5f, AnchorRight  = 0.5f,
            AnchorTop      = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft     = -140f, OffsetRight  = 140f,
            OffsetTop      = -190f, OffsetBottom = 190f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical   = Control.GrowDirection.Both,
        };

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        panel.AddChild(vbox);

        var title = new Label
        {
            Text                = "PAUSED",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 38);
        vbox.AddChild(title);

        vbox.AddChild(new HSeparator());

        var resumeBtn = MakeMenuButton("Resume");
        resumeBtn.Pressed += TogglePause;
        vbox.AddChild(resumeBtn);

        var restartBtn = MakeMenuButton("Restart");
        restartBtn.Pressed += () =>
        {
            GetTree().Paused = false;
            GetTree().ReloadCurrentScene();
        };
        vbox.AddChild(restartBtn);

        var menuBtn = MakeMenuButton("Main Menu");
        menuBtn.Pressed += () =>
        {
            GetTree().Paused = false;
            GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        };
        vbox.AddChild(menuBtn);

        _pauseOverlay.AddChild(panel);
        AddChild(_pauseOverlay);
    }

    // ── Game over screen ──────────────────────────────────────────────────────

    private void BuildGameOverScreen()
    {
        _gameOverOverlay = new ColorRect
        {
            AnchorRight  = 1f, AnchorBottom = 1f,
            Color        = new Color(0.08f, 0f, 0f, 0.88f),
            Visible      = false,
        };

        var panel = new PanelContainer
        {
            AnchorLeft     = 0.5f, AnchorRight  = 0.5f,
            AnchorTop      = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft     = -150f, OffsetRight  = 150f,
            OffsetTop      = -210f, OffsetBottom = 210f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical   = Control.GrowDirection.Both,
        };

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        panel.AddChild(vbox);

        var title = new Label
        {
            Text                = "GAME OVER",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 40);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.2f, 0.2f));
        vbox.AddChild(title);

        vbox.AddChild(new HSeparator());

        _goScoreLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _goScoreLabel.AddThemeFontSizeOverride("font_size", 24);
        vbox.AddChild(_goScoreLabel);

        _goWaveLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _goWaveLabel.AddThemeFontSizeOverride("font_size", 24);
        vbox.AddChild(_goWaveLabel);

        vbox.AddChild(new HSeparator());

        var playAgainBtn = MakeMenuButton("Play Again");
        playAgainBtn.Pressed += () => GetTree().ReloadCurrentScene();
        vbox.AddChild(playAgainBtn);

        var menuBtn = MakeMenuButton("Main Menu");
        menuBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        vbox.AddChild(menuBtn);

        _gameOverOverlay.AddChild(panel);
        AddChild(_gameOverOverlay);
    }

    private static Button MakeMenuButton(string text)
    {
        var btn = new Button
        {
            Text              = text,
            CustomMinimumSize = new Vector2(220, 46),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        btn.AddThemeFontSizeOverride("font_size", 20);
        return btn;
    }

    private static ColorRect MakeRect(Color color, Vector2 size, Vector2 pos) =>
        new() { Color = color, Size = size, Position = pos };

    // ── Public API ────────────────────────────────────────────────────────────

    public void BindToPlayer(Player player, Weapon weapon)
    {
        _maxHealth = player.MaxHealth;
        UpdateHealth(player.CurrentHealth, _maxHealth);
        UpdateAmmo(weapon.CurrentAmmo, weapon.ReserveAmmo);
        UpdateWeapon(weapon.WeaponName);
    }

    public void UpdateHealth(float current, float max)
    {
        if (_healthFill == null) return;
        _healthFill.Size = new Vector2(200f * (current / max), 16f);
    }

    public void UpdateAmmo(int current, int reserve)
    {
        if (_ammoLabel == null) return;
        string c = current < 0 ? "∞" : current.ToString();
        string r = reserve < 0 ? "∞" : reserve.ToString();
        _ammoLabel.Text = $"{c} | {r}";
    }

    public void SetReloading(bool reloading)
    {
        if (_reloadLabel != null) _reloadLabel.Visible = reloading;
    }

    public void UpdateWeapon(string weaponName)
    {
        if (_weaponLabel != null) _weaponLabel.Text = weaponName;
    }

    public void AddJoysticks(VirtualJoystick move, VirtualJoystick aim)
    {
        _joystickLayer?.AddChild(move);
        _joystickLayer?.AddChild(aim);
    }

    // Called by the Resume button — goes through the same RPC path as the P key.
    public void TogglePause()
    {
        if (IsGameOver) return;
        PauseCallback?.Invoke();
    }

    // Called by Arena after applying pause state on all peers.
    public void SetPauseOverlayVisible(bool visible)
    {
        if (_pauseOverlay != null) _pauseOverlay.Visible = visible;
    }

    public void ShowGameOver(int score, int wave)
    {
        IsGameOver = true;
        if (_goScoreLabel != null) _goScoreLabel.Text = $"Score: {score}";
        if (_goWaveLabel  != null) _goWaveLabel.Text  = $"Wave reached: {wave}";
        if (_gameOverOverlay != null) _gameOverOverlay.Visible = true;
    }

    // ── Signal handlers ───────────────────────────────────────────────────────

    private void OnWaveStarted(int wave)
    {
        if (_waveLabel != null) _waveLabel.Text = $"Wave {wave}";
    }

    private void OnEnemiesChanged(int count)
    {
        if (_enemiesLabel != null) _enemiesLabel.Text = $"Enemies: {count}";
    }

    private void OnScoreChanged(int score, float multiplier)
    {
        if (_scoreLabel != null) _scoreLabel.Text = $"Score: {score}";
        if (_multiplierLabel != null)
        {
            _multiplierLabel.Visible = multiplier > 1.05f;
            _multiplierLabel.Text    = $"x{multiplier:F1}";
        }
    }

    private async void OnWeaponUnlocked(string weaponName)
    {
        if (_unlockLabel == null) return;
        _unlockLabel.Text    = $"UNLOCKED: {weaponName}!";
        _unlockLabel.Visible = true;
        await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
        if (IsInstanceValid(_unlockLabel)) _unlockLabel.Visible = false;
    }
}
