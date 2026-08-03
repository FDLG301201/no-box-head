using Godot;

namespace NoBoxHead;

public partial class HUD : CanvasLayer
{
	private Label?     _waveAnnounce;   // big zoom-in banner shown at the start of each wave
	private Label?     _pauseWaveLabel; // wave readout, now only visible in the pause menu
	private Label?     _enemiesLabel;
	private Label?     _ammoLabel;
	private Label?     _reloadLabel;
	private Label?     _scoreLabel;
	private Label?     _multiplierLabel;
	private Label?     _unlockLabel;
	private Label?     _weaponLabel;
	private ColorRect? _healthFill;
	private Control?   _joystickLayer;

	private ColorRect?      _pauseOverlay;
	private PanelContainer? _pauseMainPanel;
	private PanelContainer? _pauseSettingsPanel;
	private ColorRect? _gameOverOverlay;
	private Label?     _goScoreLabel;
	private Label?     _goWaveLabel;

	private readonly System.Collections.Generic.Dictionary<AimMode, Button> _aimButtons = new();
	private HSlider? _pauseVolumeSlider;

	// Player 2 status panel (local co-op only).
	private ColorRect? _healthFillP2;
	private Label?     _ammoLabelP2;
	private Label?     _reloadLabelP2;
	private Label?     _weaponLabelP2;

	public bool IsGameOver { get; private set; }

	// Generic feature-tag check: true on Android/iOS exports, false on desktop/editor —
	// used to gate touch-only UI (virtual joysticks, on-screen action buttons) so a
	// keyboard-and-mouse session never sees controls it doesn't need.
	public static bool IsMobile => OS.HasFeature("mobile");

	public System.Action? SwitchWeaponCallback     { get; set; }
	public System.Action? SwitchWeaponPrevCallback { get; set; }
	public System.Action? KnifeCallback            { get; set; }
	public System.Action? PauseCallback            { get; set; }

	public System.Action? SwitchWeaponCallbackP2     { get; set; }
	public System.Action? SwitchWeaponPrevCallbackP2 { get; set; }
	public System.Action? KnifeCallbackP2            { get; set; }

	private float  _maxHealth   = 100f;
	private int    _currentWave = 1;
	private Tween? _waveTween;

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
		if (SettingsManager.Instance?.GameMode == GameMode.LocalCoop)
			BuildP2Panel();
		BuildPauseMenu();
		BuildGameOverScreen();

		if (GameManager.Instance != null)
		{
			GameManager.Instance.WaveStarted             += OnWaveStarted;
			GameManager.Instance.EnemiesRemainingChanged += OnEnemiesChanged;
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

		var prevBtn = new Button
		{
			Text     = IsMobile ? "Prev" : "[E] Prev",
			Position = new Vector2(10, 96),
			Size     = new Vector2(66, 28),
		};
		prevBtn.Pressed += () => SwitchWeaponPrevCallback?.Invoke();
		AddChild(prevBtn);

		var switchBtn = new Button
		{
			Text     = IsMobile ? "Next" : "[Q] Next",
			Position = new Vector2(82, 96),
			Size     = new Vector2(66, 28),
		};
		switchBtn.Pressed += () => SwitchWeaponCallback?.Invoke();
		AddChild(switchBtn);

		// Knife has a keyboard shortcut on desktop (V / numpad); touch has no keyboard at all,
		// so it needs its own tappable button.
		if (IsMobile)
		{
			var knifeBtn = new Button
			{
				Text     = "Knife",
				Position = new Vector2(154, 96),
				Size     = new Vector2(66, 28),
			};
			knifeBtn.Pressed += () => KnifeCallback?.Invoke();
			AddChild(knifeBtn);

			// Desktop opens pause via P/Escape; touch has no keyboard, so it needs a button.
			var pauseBtn = new Button
			{
				Text                = "Pause",
				AnchorLeft          = 1f, AnchorRight = 1f,
				Position            = new Vector2(-70, 90),
				Size                = new Vector2(60, 32),
			};
			pauseBtn.Pressed += () => { if (!IsGameOver) PauseCallback?.Invoke(); };
			AddChild(pauseBtn);
		}

		// Wave banner: hidden by default, zooms in briefly whenever a wave starts.
		// Full-rect so it can scale around the screen centre; ignores mouse input.
		_waveAnnounce = new Label
		{
			Text                = "",
			AnchorRight         = 1f, AnchorBottom = 1f,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment   = VerticalAlignment.Center,
			MouseFilter         = Control.MouseFilterEnum.Ignore,
			Visible             = false,
		};
		_waveAnnounce.AddThemeFontSizeOverride("font_size", 64);
		_waveAnnounce.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
		_waveAnnounce.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
		_waveAnnounce.AddThemeConstantOverride("outline_size", 8);
		AddChild(_waveAnnounce);

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

	// ── P2 status panel (local co-op) ─────────────────────────────────────────

	private void BuildP2Panel()
	{
		// Container anchored to the right half so it overlays P2's split viewport.
		var panel = new Control
		{
			AnchorLeft   = 0.5f, AnchorRight  = 1f,
			AnchorTop    = 0f,   AnchorBottom = 1f,
			MouseFilter  = Control.MouseFilterEnum.Ignore,
		};
		AddChild(panel);

		// Tag label.
		var tag = new Label { Text = "P2", Position = new Vector2(10, -2) };
		tag.AddThemeFontSizeOverride("font_size", 11);
		tag.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
		panel.AddChild(tag);

		// Health bar.
		panel.AddChild(MakeRect(new Color(0.15f, 0.15f, 0.15f), new Vector2(200, 16), new Vector2(10, 10)));
		_healthFillP2 = MakeRect(new Color(0.2f, 0.85f, 0.2f), new Vector2(200, 16), new Vector2(10, 10));
		panel.AddChild(_healthFillP2);

		var hpLbl = new Label { Text = "HP", Position = new Vector2(10, 8) };
		hpLbl.AddThemeFontSizeOverride("font_size", 11);
		panel.AddChild(hpLbl);

		// Ammo.
		_ammoLabelP2 = new Label { Text = "-- | --", Position = new Vector2(10, 34) };
		_ammoLabelP2.AddThemeFontSizeOverride("font_size", 18);
		panel.AddChild(_ammoLabelP2);

		// Reload.
		_reloadLabelP2 = new Label { Text = "RELOADING...", Position = new Vector2(10, 58), Visible = false };
		_reloadLabelP2.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.2f));
		_reloadLabelP2.AddThemeFontSizeOverride("font_size", 16);
		panel.AddChild(_reloadLabelP2);

		// Weapon name.
		_weaponLabelP2 = new Label { Text = "Pistol", Position = new Vector2(10, 76) };
		_weaponLabelP2.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
		_weaponLabelP2.AddThemeFontSizeOverride("font_size", 14);
		panel.AddChild(_weaponLabelP2);

		// P2 normally switches weapons via numpad, which doesn't exist on a touch device.
		if (IsMobile)
		{
			var prevBtn = new Button { Text = "Prev", Position = new Vector2(10, 96), Size = new Vector2(66, 28) };
			prevBtn.Pressed += () => SwitchWeaponPrevCallbackP2?.Invoke();
			panel.AddChild(prevBtn);

			var nextBtn = new Button { Text = "Next", Position = new Vector2(82, 96), Size = new Vector2(66, 28) };
			nextBtn.Pressed += () => SwitchWeaponCallbackP2?.Invoke();
			panel.AddChild(nextBtn);

			var knifeBtn = new Button { Text = "Knife", Position = new Vector2(154, 96), Size = new Vector2(66, 28) };
			knifeBtn.Pressed += () => KnifeCallbackP2?.Invoke();
			panel.AddChild(knifeBtn);
		}
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
			OffsetLeft     = -160f, OffsetRight  = 160f,
			OffsetTop      = -250f, OffsetBottom = 250f,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical   = Control.GrowDirection.Both,
		};

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 12);
		panel.AddChild(vbox);

		var title = new Label
		{
			Text                = "PAUSED",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", 38);
		vbox.AddChild(title);

		// Wave readout lives here now instead of cluttering the in-game HUD.
		_pauseWaveLabel = new Label
		{
			Text                = "Wave 1",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		_pauseWaveLabel.AddThemeFontSizeOverride("font_size", 22);
		_pauseWaveLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
		vbox.AddChild(_pauseWaveLabel);

		vbox.AddChild(new HSeparator());

		var resumeBtn = MakeMenuButton("Resume");
		resumeBtn.Pressed += TogglePause;
		vbox.AddChild(resumeBtn);

		var settingsBtn = MakeMenuButton("Settings");
		settingsBtn.Pressed += () => ShowPauseSettings(true);
		vbox.AddChild(settingsBtn);

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

		_pauseMainPanel = panel;
		_pauseOverlay.AddChild(panel);
		BuildPauseSettingsPanel();
		AddChild(_pauseOverlay);
	}

	// In-pause settings sub-panel. Swaps places with the main pause panel.
	private void BuildPauseSettingsPanel()
	{
		var panel = new PanelContainer
		{
			AnchorLeft     = 0.5f, AnchorRight  = 0.5f,
			AnchorTop      = 0.5f, AnchorBottom = 0.5f,
			OffsetLeft     = -160f, OffsetRight  = 160f,
			OffsetTop      = -250f, OffsetBottom = 250f,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical   = Control.GrowDirection.Both,
			Visible        = false,
		};

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 12);
		panel.AddChild(vbox);

		var title = new Label
		{
			Text                = "SETTINGS",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", 32);
		vbox.AddChild(title);

		vbox.AddChild(new HSeparator());

		BuildPauseAimControls(vbox);
		BuildPauseVolumeControl(vbox);

		vbox.AddChild(new HSeparator());

		var backBtn = MakeMenuButton("Back");
		backBtn.Pressed += () => ShowPauseSettings(false);
		vbox.AddChild(backBtn);

		_pauseSettingsPanel = panel;
		_pauseOverlay!.AddChild(panel);
	}

	private void ShowPauseSettings(bool show)
	{
		if (_pauseMainPanel     != null) _pauseMainPanel.Visible     = !show;
		if (_pauseSettingsPanel != null) _pauseSettingsPanel.Visible = show;
		if (!show) return;

		RefreshPauseAimButtons();
		if (_pauseVolumeSlider != null && SettingsManager.Instance != null)
			_pauseVolumeSlider.SetValueNoSignal(SettingsManager.Instance.SfxVolume);
	}

	// Aim-mode switcher mirrored from the Settings screen so it can be changed mid-run.
	private void BuildPauseAimControls(VBoxContainer parent)
	{
		var label = new Label
		{
			Text                = "Aim Mode",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		label.AddThemeFontSizeOverride("font_size", 16);
		parent.AddChild(label);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		parent.AddChild(row);

		bool isCoop = SettingsManager.Instance?.GameMode == GameMode.LocalCoop;

		foreach (var mode in new[] { AimMode.Movement, AimMode.Mouse, AimMode.AutoAim })
		{
			var btn = new Button
			{
				Text                = mode switch
				{
					AimMode.Mouse   => "Mouse",
					AimMode.AutoAim => "Auto",
					_               => "Move",
				},
				ToggleMode          = true,
				CustomMinimumSize   = new Vector2(0, 38),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				// Mouse aim needs a single cursor, so it stays unavailable in local co-op.
				Disabled            = isCoop && mode == AimMode.Mouse,
			};
			btn.AddThemeFontSizeOverride("font_size", 15);

			var captured = mode;
			btn.Pressed += () =>
			{
				if (SettingsManager.Instance == null) return;
				SettingsManager.Instance.AimMode = captured;
				SettingsManager.Instance.SaveSettings();
				RefreshPauseAimButtons();
			};

			_aimButtons[mode] = btn;
			row.AddChild(btn);
		}

		RefreshPauseAimButtons();
	}

	private void BuildPauseVolumeControl(VBoxContainer parent)
	{
		var label = new Label
		{
			Text                = "Sound Effects",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		label.AddThemeFontSizeOverride("font_size", 16);
		parent.AddChild(label);

		var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 38) };
		parent.AddChild(row);

		var slider = new HSlider
		{
			MinValue            = 0,
			MaxValue            = 1,
			Step                = 0.05,
			Value               = SettingsManager.Instance?.SfxVolume ?? 0.8f,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical   = Control.SizeFlags.ShrinkCenter,
		};
		row.AddChild(slider);

		var readout = new Label
		{
			CustomMinimumSize = new Vector2(56, 0),
			VerticalAlignment = VerticalAlignment.Center,
		};
		readout.AddThemeFontSizeOverride("font_size", 14);
		row.AddChild(readout);

		void Refresh(double v) => readout.Text = v <= 0.001 ? "Muted" : $"{Mathf.RoundToInt((float)v * 100)}%";
		Refresh(slider.Value);

		slider.ValueChanged += v =>
		{
			if (SettingsManager.Instance == null) return;
			SettingsManager.Instance.SfxVolume = (float)v;
			SettingsManager.Instance.SaveSettings();
			AudioManager.Instance?.ApplyVolume();
			Refresh(v);
		};

		_pauseVolumeSlider = slider;
	}

	private void RefreshPauseAimButtons()
	{
		var current = SettingsManager.Instance?.AimMode ?? AimMode.Movement;
		foreach (var (mode, btn) in _aimButtons)
			if (IsInstanceValid(btn)) btn.ButtonPressed = mode == current;
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

	// ── P2 public API ─────────────────────────────────────────────────────────

	public void BindToPlayerP2(Player player, Weapon weapon)
	{
		UpdateHealthP2(player.CurrentHealth, player.MaxHealth);
		UpdateAmmoP2(weapon.CurrentAmmo, weapon.ReserveAmmo);
		UpdateWeaponP2(weapon.WeaponName);
	}

	public void UpdateHealthP2(float current, float max)
	{
		if (_healthFillP2 == null) return;
		_healthFillP2.Size = new Vector2(200f * (current / max), 16f);
	}

	public void UpdateAmmoP2(int current, int reserve)
	{
		if (_ammoLabelP2 == null) return;
		string c = current < 0 ? "∞" : current.ToString();
		string r = reserve < 0 ? "∞" : reserve.ToString();
		_ammoLabelP2.Text = $"{c} | {r}";
	}

	public void SetReloadingP2(bool reloading)
	{
		if (_reloadLabelP2 != null) _reloadLabelP2.Visible = reloading;
	}

	public void UpdateWeaponP2(string weaponName)
	{
		if (_weaponLabelP2 != null) _weaponLabelP2.Text = weaponName;
	}

	/// <summary>
	/// Places a player's move/aim joysticks bottom-left/bottom-right of their share of the
	/// screen — the full screen solo, or their half in local co-op (mirroring the split
	/// viewport each already plays in).
	/// </summary>
	public void AddJoysticks(VirtualJoystick move, VirtualJoystick aim, int playerIndex, bool isCoop)
	{
		if (_joystickLayer == null) return;

		var half = new Control
		{
			AnchorLeft   = isCoop && playerIndex == 1 ? 0.5f : 0f,
			AnchorRight  = isCoop && playerIndex == 0 ? 0.5f : 1f,
			AnchorTop    = 0f, AnchorBottom = 1f,
			MouseFilter  = Control.MouseFilterEnum.Ignore,
		};
		_joystickLayer.AddChild(half);

		const float margin = 100f;
		// VirtualJoystick now owns its rect exactly (Size = Radius*2), so it's placed like any
		// other Control — by its top-left corner — with Radius subtracted to land the desired
		// visual centre at (margin, -margin) from the anchored corner.
		move.AnchorLeft = 0f; move.AnchorRight = 0f; move.AnchorTop = 1f; move.AnchorBottom = 1f;
		move.Position   = new Vector2(margin - move.Radius, -margin - move.Radius);
		half.AddChild(move);

		aim.AnchorLeft = 1f; aim.AnchorRight = 1f; aim.AnchorTop = 1f; aim.AnchorBottom = 1f;
		aim.Position   = new Vector2(-margin - aim.Radius, -margin - aim.Radius);
		half.AddChild(aim);
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
		// Always reopen on the main page rather than wherever the last session left off.
		ShowPauseSettings(false);
		if (!visible) return;
		// Refresh on open: the wave may have advanced and aim mode can change elsewhere.
		if (_pauseWaveLabel != null) _pauseWaveLabel.Text = $"Wave {_currentWave}";
	}

	public void ShowGameOver(int score, int wave)
	{
		IsGameOver = true;
		AudioManager.Instance?.Play(AudioManager.GameOver);
		if (_goScoreLabel != null) _goScoreLabel.Text = $"Score: {score}";
		if (_goWaveLabel  != null) _goWaveLabel.Text  = $"Wave reached: {wave}";
		if (_gameOverOverlay != null) _gameOverOverlay.Visible = true;
	}

	// ── Signal handlers ───────────────────────────────────────────────────────

	private void OnWaveStarted(int wave)
	{
		_currentWave = wave;
		if (_pauseWaveLabel != null) _pauseWaveLabel.Text = $"Wave {wave}";
		ShowWaveAnnouncement(wave);
		AudioManager.Instance?.Play(AudioManager.WaveStart);
	}

	// Punchy zoom-in banner: overshoots past full size, settles, holds, then fades out.
	private void ShowWaveAnnouncement(int wave)
	{
		if (_waveAnnounce == null) return;

		// Restart cleanly if waves come faster than the animation.
		if (_waveTween != null && _waveTween.IsValid()) _waveTween.Kill();

		_waveAnnounce.Text     = $"WAVE {wave}";
		_waveAnnounce.Visible  = true;
		_waveAnnounce.Modulate = Colors.White;
		// Label is full-rect, so the screen centre is its centre. Taken from the viewport
		// rather than Size, which is still zero before the first layout pass.
		_waveAnnounce.PivotOffset = GetViewport().GetVisibleRect().Size / 2f;
		_waveAnnounce.Scale       = new Vector2(0.3f, 0.3f);

		_waveTween = CreateTween();
		_waveTween.TweenProperty(_waveAnnounce, "scale", new Vector2(1.15f, 1.15f), 0.32)
				  .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		_waveTween.TweenProperty(_waveAnnounce, "scale", Vector2.One, 0.12);
		_waveTween.TweenInterval(0.85);
		_waveTween.TweenProperty(_waveAnnounce, "modulate:a", 0f, 0.45);
		_waveTween.TweenCallback(Callable.From(() =>
		{
			if (IsInstanceValid(_waveAnnounce)) _waveAnnounce.Visible = false;
		}));
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
