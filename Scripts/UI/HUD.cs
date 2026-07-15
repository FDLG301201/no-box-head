using Godot;

namespace NoBoxHead;

/// <summary>
/// Heads-Up Display shown during gameplay.
/// Bound to the local player and weapon after they are spawned.
/// </summary>
public partial class HUD : CanvasLayer
{
    private Label? _waveLabel;
    private Label? _enemiesLabel;
    private Label? _ammoLabel;
    private Label? _reloadLabel;
    private ColorRect? _healthFill;
    private Control? _joystickLayer;

    private float _maxHealth = 100f;

    public override void _Ready()
    {
        Layer = 10;
        BuildHUD();

        // Connect game signals.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.WaveStarted += OnWaveStarted;
            GameManager.Instance.EnemiesRemainingChanged += OnEnemiesChanged;
            GameManager.Instance.WaveCompleted += waveNum =>
            {
                if (_waveLabel != null)
                    _waveLabel.Text = $"Wave {waveNum} complete!";
            };
        }
    }

    // ── Building ──────────────────────────────────────────────────────────────

    private void BuildHUD()
    {
        // Health bar.
        var hbBg = MakeRect(new Color(0.15f, 0.15f, 0.15f), new Vector2(200, 16), new Vector2(10, 10));
        AddChild(hbBg);

        _healthFill = MakeRect(new Color(0.2f, 0.85f, 0.2f), new Vector2(200, 16), new Vector2(10, 10));
        AddChild(_healthFill);

        var hpLabel = new Label
        {
            Text = "HP",
            Position = new Vector2(10, 8)
        };
        hpLabel.AddThemeFontSizeOverride("font_size", 11);
        AddChild(hpLabel);

        // Ammo label.
        _ammoLabel = new Label
        {
            Text = "12 / 12",
            Position = new Vector2(10, 34)
        };
        _ammoLabel.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_ammoLabel);

        // Reload label.
        _reloadLabel = new Label
        {
            Text = "RELOADING...",
            Position = new Vector2(10, 58),
            Visible = false
        };
        _reloadLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.2f));
        _reloadLabel.AddThemeFontSizeOverride("font_size", 16);
        AddChild(_reloadLabel);

        // Wave label (centered top).
        _waveLabel = new Label
        {
            Text = "Wave 1",
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            Position = new Vector2(-60, 10),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _waveLabel.AddThemeFontSizeOverride("font_size", 22);
        AddChild(_waveLabel);

        // Enemies remaining (top right).
        _enemiesLabel = new Label
        {
            Text = "Enemies: 0",
            AnchorLeft = 1f, AnchorRight = 1f,
            Position = new Vector2(-160, 10)
        };
        _enemiesLabel.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_enemiesLabel);

        // Joystick container.
        _joystickLayer = new Control
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        AddChild(_joystickLayer);
    }

    private static ColorRect MakeRect(Color color, Vector2 size, Vector2 pos) =>
        new() { Color = color, Size = size, Position = pos };

    // ── Public API ────────────────────────────────────────────────────────────

    public void BindToPlayer(Player player, Weapon weapon)
    {
        _maxHealth = player.MaxHealth;
        UpdateHealth(player.CurrentHealth, _maxHealth);
        UpdateAmmo(weapon.CurrentAmmo, weapon.MagazineSize);
    }

    public void UpdateHealth(float current, float max)
    {
        if (_healthFill == null) return;
        _healthFill.Size = new Vector2(200f * (current / max), 16f);
    }

    public void UpdateAmmo(int current, int max)
    {
        if (_ammoLabel != null) _ammoLabel.Text = $"{current} / {max}";
    }

    public void SetReloading(bool reloading)
    {
        if (_reloadLabel != null) _reloadLabel.Visible = reloading;
    }

    public void AddJoysticks(VirtualJoystick move, VirtualJoystick aim)
    {
        _joystickLayer?.AddChild(move);
        _joystickLayer?.AddChild(aim);
    }

    // ── Game signal handlers ──────────────────────────────────────────────────

    private void OnWaveStarted(int wave)
    {
        if (_waveLabel != null) _waveLabel.Text = $"Wave {wave}";
    }

    private void OnEnemiesChanged(int count)
    {
        if (_enemiesLabel != null) _enemiesLabel.Text = $"Enemies: {count}";
    }
}
