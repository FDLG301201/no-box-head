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

	private static bool IsMobile => Platform.IsMobile;

	// ── Per-player screen halves ──────────────────────────────────────────────

	/// <summary>
	/// Builds a player's screen-half. <c>Root</c> is what the caller adds to the scene tree
	/// (it holds the real screen-split anchors); <c>Content</c> is what the caller adds its
	/// own children to. Outside tabletop mode these are the same Control. In tabletop mode
	/// they differ: Content is nested inside Root with swapped (landscape) dimensions and a
	/// 90° rotation (see RotateHalfContent), so a caller can add children with the usual
	/// bottom-left/bottom-right anchors as if it were a normal landscape half — none of the
	/// rotation math leaks out.
	/// </summary>
	private (Control Root, Control Content) MakePlayerHalf(int playerIndex, bool isCoop)
	{
		var outer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };

		if (!isCoop)
		{
			outer.AnchorLeft = 0f; outer.AnchorRight  = 1f;
			outer.AnchorTop  = 0f; outer.AnchorBottom = 1f;
			return (outer, outer);
		}

		// Same plain vertical divider either way — tabletop's rotation is applied below, not
		// to the split itself.
		outer.AnchorLeft   = playerIndex == 1 ? 0.5f : 0f;
		outer.AnchorRight  = playerIndex == 0 ? 0.5f : 1f;
		outer.AnchorTop    = 0f; outer.AnchorBottom = 1f;

		float rotation = Platform.GetHalfRotation(playerIndex);
		if (Mathf.IsZeroApprox(rotation)) return (outer, outer);

		// Tabletop always splits the full device screen straight 50/50 left/right — the only
		// shape this ever needs — computed directly from the live window size rather than a
		// Resized signal, which for the equivalent camera-side wrapper never reliably fired
		// (see CameraManager.WrapForHalf); computing it once up front sidesteps that entirely.
		var screenSize = GetViewport().GetVisibleRect().Size;
		var halfSize   = new Vector2(screenSize.X / 2f, screenSize.Y);
		return (outer, RotateHalfContent(outer, rotation, halfSize));
	}

	/// <summary>
	/// Nests a landscape-dimensioned, rotated content Control inside <paramref name="outer"/>
	/// and returns it. Mirrors CameraManager.WrapForHalf: the outer Control keeps plain
	/// unrotated anchors (the actual screen split), while the inner one gets the physical
	/// half's real on-screen size (<paramref name="physicalSize"/>) swapped and rotated, so
	/// content laid out normally inside it — joystick margins, button positions, all of it —
	/// lands correctly once turned to fit.
	/// </summary>
	private static Control RotateHalfContent(Control outer, float rotation, Vector2 physicalSize)
	{
		var inner = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Rotation = rotation };
		outer.AddChild(inner);

		var swapped = new Vector2(physicalSize.Y, physicalSize.X);
		inner.Size        = swapped;
		inner.PivotOffset = swapped / 2f;
		// Pivot (Position + PivotOffset) lands on the half's own centre, so the rotated
		// footprint — swapped dims turned back to (physicalSize.X, physicalSize.Y) — exactly
		// fills its bounds.
		inner.Position = physicalSize / 2f - swapped / 2f;
		return inner;
	}

	// Desktop-only HUD buttons; touch controls wire straight to the Player in AddTouchControls.
	public System.Action? SwitchWeaponCallback     { get; set; }
	public System.Action? SwitchWeaponPrevCallback { get; set; }
	public System.Action? PauseCallback            { get; set; }

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
		BuildBossBars();
		BuildPauseMenu();
		BuildGameOverScreen();

		if (GameManager.Instance != null)
		{
			GameManager.Instance.WaveStarted             += OnWaveStarted;
			GameManager.Instance.EnemiesRemainingChanged += OnEnemiesChanged;
			GameManager.Instance.BossSpawned             += OnBossSpawned;
			GameManager.Instance.BossDefeated            += OnBossDefeated;

			// A boss may already be alive if this HUD was rebuilt mid-fight (scene reload).
			if (GameManager.Instance.ActiveBoss is { } boss && IsInstanceValid(boss))
				OnBossSpawned(boss);
		}

		if (ScoreManager.Instance != null)
		{
			ScoreManager.Instance.ScoreChanged   += OnScoreChanged;
			ScoreManager.Instance.WeaponUnlocked += OnWeaponUnlocked;
		}
	}

	// GameManager/ScoreManager are autoloads — they outlive this HUD across a scene reload
	// (Restart/Play Again). Without unsubscribing, the NEXT playthrough's signals would also
	// fire on this disposed instance, throwing ObjectDisposedException from these very
	// handlers the moment they touched a freed Label.
	public override void _ExitTree()
	{
		if (GameManager.Instance != null)
		{
			GameManager.Instance.WaveStarted             -= OnWaveStarted;
			GameManager.Instance.EnemiesRemainingChanged -= OnEnemiesChanged;
			GameManager.Instance.BossSpawned             -= OnBossSpawned;
			GameManager.Instance.BossDefeated            -= OnBossDefeated;
		}
		if (ScoreManager.Instance != null)
		{
			ScoreManager.Instance.ScoreChanged   -= OnScoreChanged;
			ScoreManager.Instance.WeaponUnlocked -= OnWeaponUnlocked;
		}
	}

	// ── HUD elements ──────────────────────────────────────────────────────────

	private void BuildHUD()
	{
		// P1's status belongs over P1's own half of the split, turned to read from that
		// player's seat — the same treatment BuildP2Panel already gives P2. Adding it straight
		// to the HUD root left it unrotated in the screen's top-left corner, spilling across
		// the divider instead of sitting in P1's viewport. Solo play has no halves, so there
		// the HUD root still is the whole screen and nothing changes.
		bool isCoop  = UsesSplitScreen;
		Node target = this;
		if (isCoop)
		{
			var (halfRoot, half) = MakePlayerHalf(0, isCoop: true);
			AddChild(halfRoot);
			target = half;

			var tag = new Label { Text = "P1", Position = new Vector2(10, -2) };
			tag.AddThemeFontSizeOverride("font_size", 11);
			tag.AddThemeColorOverride("font_color", new Color(0.2f, 0.4f, 1f));
			half.AddChild(tag);
		}

		target.AddChild(MakeRect(new Color(0.15f, 0.15f, 0.15f), new Vector2(200, 16), new Vector2(10, 10)));
		_healthFill = MakeRect(new Color(0.2f, 0.85f, 0.2f), new Vector2(200, 16), new Vector2(10, 10));
		target.AddChild(_healthFill);

		var hpLabel = new Label { Text = "HP", Position = new Vector2(10, 8) };
		hpLabel.AddThemeFontSizeOverride("font_size", 11);
		target.AddChild(hpLabel);

		_ammoLabel = new Label { Text = "12 | 12", Position = new Vector2(10, 34) };
		_ammoLabel.AddThemeFontSizeOverride("font_size", 18);
		target.AddChild(_ammoLabel);

		_reloadLabel = new Label
		{
			Text     = "RELOADING...",
			Position = new Vector2(10, 58),
			Visible  = false,
		};
		_reloadLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.2f));
		_reloadLabel.AddThemeFontSizeOverride("font_size", 16);
		target.AddChild(_reloadLabel);

		_weaponLabel = new Label { Text = "Pistol", Position = new Vector2(10, 76) };
		_weaponLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
		_weaponLabel.AddThemeFontSizeOverride("font_size", 14);
		target.AddChild(_weaponLabel);

		// Weapon-switch buttons. On touch these live around the aim stick instead (see
		// AddTouchControls), so here they're desktop-only mouse shortcuts.
		if (!IsMobile)
		{
			var prevBtn = new Button
			{
				Text     = "[E] Prev",
				Position = new Vector2(10, 96),
				Size     = new Vector2(100, 28),
			};
			prevBtn.Pressed += () => SwitchWeaponPrevCallback?.Invoke();
			target.AddChild(prevBtn);

			var switchBtn = new Button
			{
				Text     = "[Q] Next",
				Position = new Vector2(116, 96),
				Size     = new Vector2(100, 28),
			};
			switchBtn.Pressed += () => SwitchWeaponCallback?.Invoke();
			target.AddChild(switchBtn);
		}
		else
		{
			// Desktop opens pause via P/Escape; touch has no keyboard, so it needs a button.
			var pauseBtn = new Button
			{
				Text                = "II",
				AnchorLeft          = 1f, AnchorRight = 1f,
				Position            = new Vector2(-66, 90),
				Size                = new Vector2(56, 40),
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

	/// <summary>
	/// True only when the display is actually carved into per-player halves. Being in local
	/// co-op is not enough on its own: with Shared Camera both players look at one full-screen
	/// view, so the HUD has to lay its panels out on that single screen rather than inside
	/// halves that do not exist. On mobile CameraMode is always SplitScreen, so tabletop is
	/// unaffected.
	/// </summary>
	private static bool UsesSplitScreen =>
		SettingsManager.Instance?.GameMode   == GameMode.LocalCoop &&
		SettingsManager.Instance?.CameraMode == CameraMode.SplitScreen;

	private void BuildP2Panel()
	{
		// Overlays P2's split viewport, wherever that is on this platform.
		var (root, panel) = MakePlayerHalf(1, isCoop: UsesSplitScreen);
		AddChild(root);

		// With one shared screen both status panels land on it, and P1's already owns the top
		// left — push P2's to the opposite corner so they do not stack on top of each other.
		if (!UsesSplitScreen)
		{
			var right = new Control
			{
				AnchorLeft = 1f, AnchorRight = 1f,
				Position   = new Vector2(-220, 0),
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			panel.AddChild(right);
			panel = right;
		}

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

		// On touch, P2's action buttons live around their own joysticks (AddTouchControls).
	}

	// ── Boss health bar ───────────────────────────────────────────────────────

	private readonly List<ColorRect> _bossFills = new();
	private readonly List<Control>   _bossRoots = new();
	private readonly List<Label>     _bossNames = new();
	private Node? _trackedBoss;

	/// <summary>
	/// One bar PER SCREEN HALF rather than a single shared one at the top of the display.
	/// In tabletop co-op each half is rotated 90° to face its own player, so a shared bar would
	/// read sideways for both of them and belong to neither. MakePlayerHalf collapses to the
	/// full screen outside co-op, so solo and online still get exactly one bar.
	/// </summary>
	private void BuildBossBars()
	{
		// One bar per half only when the screen really is split; a shared view gets a single bar.
		bool isCoop = UsesSplitScreen;
		int halves  = isCoop ? 2 : 1;

		for (int i = 0; i < halves; i++)
		{
			var (root, panel) = MakePlayerHalf(i, isCoop);
			AddChild(root);
			root.Visible = false;
			_bossRoots.Add(root);

			// Anchored to the top edge of its own half, centred horizontally.
			var holder = new Control
			{
				AnchorLeft = 0.5f, AnchorRight = 0.5f,
				Position   = new Vector2(-150, 118),
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			panel.AddChild(holder);

			var name = new Label
			{
				Text = "", Position = new Vector2(0, -20),
				HorizontalAlignment = HorizontalAlignment.Center,
				CustomMinimumSize   = new Vector2(300, 0),
				MouseFilter         = Control.MouseFilterEnum.Ignore,
			};
			name.AddThemeFontSizeOverride("font_size", 14);
			name.AddThemeColorOverride("font_color", new Color(1f, 0.75f, 0.3f));
			holder.AddChild(name);
			_bossNames.Add(name);

			holder.AddChild(MakeRect(new Color(0.12f, 0.12f, 0.12f), new Vector2(300, 12), Vector2.Zero));
			var fill = MakeRect(new Color(0.85f, 0.15f, 0.15f), new Vector2(300, 12), Vector2.Zero);
			holder.AddChild(fill);
			_bossFills.Add(fill);
		}
	}

	private void OnBossSpawned(Node boss)
	{
		_trackedBoss = boss;
		string label = boss is Boss b ? b.BossName : "BOSS";
		foreach (var n in _bossNames) n.Text = label;
		foreach (var r in _bossRoots) r.Visible = true;
	}

	private void OnBossDefeated()
	{
		_trackedBoss = null;
		foreach (var r in _bossRoots) r.Visible = false;
	}

	// Polled rather than pushed: the boss's health already reaches every peer through
	// EnemyBase's sync, so reading it here keeps the bar correct on clients without adding
	// another RPC purely to feed a UI widget.
	public override void _Process(double delta)
	{
		if (_trackedBoss is not Boss boss || !IsInstanceValid(boss)) return;
		float f = boss.HealthFraction;
		foreach (var fill in _bossFills)
			fill.Size = new Vector2(300f * f, 12f);
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

		var restartBtn = MakeMenuButton("Restart", danger: true);
		restartBtn.Pressed += () =>
		{
			GetTree().Paused = false;
			GetTree().ReloadCurrentScene();
		};
		vbox.AddChild(restartBtn);

		var menuBtn = MakeMenuButton("Main Menu", danger: true);
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

		var backBtn = MakeMenuButton("Back", danger: true);
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

		// Mouse aim needs a single cursor: unusable split-screen, absent entirely on touch.
		bool noMouseAim = SettingsManager.Instance?.GameMode == GameMode.LocalCoop || IsMobile;

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
				Disabled            = noMouseAim && mode == AimMode.Mouse,
			};
			btn.AddThemeFontSizeOverride("font_size", 15);

			var captured = mode;
			btn.Pressed += () =>
			{
				if (SettingsManager.Instance == null) return;
				SettingsManager.Instance.AimMode = captured;
				SettingsManager.Instance.SaveSettings();
				RefreshPauseAimButtons();
				if (IsMobile) RefreshTouchAimClusters();
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

		var menuBtn = MakeMenuButton("Main Menu", danger: true);
		menuBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
		vbox.AddChild(menuBtn);

		_gameOverOverlay.AddChild(panel);
		AddChild(_gameOverOverlay);
	}

	private static Button MakeMenuButton(string text, bool danger = false)
	{
		var btn = new Button
		{
			Text              = text,
			CustomMinimumSize = new Vector2(220, 46),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			ThemeTypeVariation  = danger ? "ButtonDanger" : "",
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

	// Matches VirtualJoystick's default Radius — used to size/place the aim-side cluster even
	// when it's showing the FIRE button instead of an actual stick.
	private const float AimZoneRadius = 92f;
	private const float TouchMargin   = 118f; // screen-corner-to-stick-centre distance

	// One entry per local touch player, kept so the aim-side cluster (stick vs FIRE button)
	// can be torn down and rebuilt in place when the aim mode changes mid-run.
	private sealed class TouchPlayerContext
	{
		public Control Half = null!;
		public Player  Player = null!;
		public Control? AimCluster;
	}
	private readonly System.Collections.Generic.List<TouchPlayerContext> _touchPlayers = new();

	/// <summary>
	/// Builds a player's full touch control cluster inside their share of the screen — the
	/// whole screen solo, or their half in local co-op (mirroring the split viewport they
	/// already play in). Move stick + knife live on the left always; the right side depends on
	/// the current aim mode (see BuildAimCluster) and can change live from the pause menu.
	/// </summary>
	public void AddTouchControls(VirtualJoystick move, Player player, bool isCoop)
	{
		if (_joystickLayer == null) return;

		// Same half (and same 90° turn in tabletop mode) the player's viewport uses, so the
		// controls sit under their hands and read the right way up from their seat.
		var (root, half) = MakePlayerHalf(player.PlayerIndex, isCoop);
		_joystickLayer.AddChild(root);

		// VirtualJoystick owns its rect exactly (Size = Radius*2), so it's placed like any
		// other Control — by its top-left corner — with Radius subtracted to land the desired
		// visual centre at `TouchMargin` from the anchored corner.
		move.AnchorLeft = 0f; move.AnchorRight = 0f; move.AnchorTop = 1f; move.AnchorBottom = 1f;
		move.Position   = new Vector2(TouchMargin - move.Radius, -TouchMargin - move.Radius);
		half.AddChild(move);

		AddTouchButton(half, "Knife", new Vector2(90, 52), fromLeft: true,
			centre: new Vector2(TouchMargin, -TouchMargin - move.Radius - 42f),
			onPressed: player.ToggleKnife);

		AddTouchButton(half, "Next", new Vector2(80, 50), fromLeft: false,
			centre: new Vector2(-TouchMargin - AimZoneRadius - 58f, -TouchMargin),
			onPressed: player.SwitchToNextWeapon);

		AddTouchButton(half, "Prev", new Vector2(80, 50), fromLeft: false,
			centre: new Vector2(-TouchMargin - AimZoneRadius - 58f, -TouchMargin - 62f),
			onPressed: player.SwitchToPreviousWeapon);

		var ctx = new TouchPlayerContext { Half = half, Player = player };
		_touchPlayers.Add(ctx);
		BuildAimCluster(ctx);
	}

	/// <summary>
	/// (Re)builds the aim-mode-dependent half of a player's controls: a twin-stick aim
	/// joystick (drag-to-aim-and-fire) when the aim mode needs manual direction, or a single
	/// FIRE button when it doesn't — Auto-Aim already points at the nearest enemy, so a stick
	/// there would have nothing to steer and would just be in the way. Mouse aim is never
	/// selectable on mobile (no cursor), so the only choices reaching here are Movement and
	/// Auto-Aim.
	/// </summary>
	private void BuildAimCluster(TouchPlayerContext ctx)
	{
		if (ctx.AimCluster != null && IsInstanceValid(ctx.AimCluster))
			ctx.AimCluster.QueueFree();
		ctx.Player.SetTouchFireHeld(false); // defensive: don't leave fire latched across a rebuild

		var cluster = new Control
		{
			AnchorLeft = 1f, AnchorRight = 1f, AnchorTop = 1f, AnchorBottom = 1f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		ctx.Half.AddChild(cluster);
		ctx.AimCluster = cluster;

		bool autoAim = SettingsManager.Instance?.AimMode == AimMode.AutoAim;

		if (autoAim)
		{
			// One big, easy-to-hit trigger — there's no direction to pick, just when to shoot.
			var fire = AddTouchButton(cluster, "FIRE", Vector2.One * (AimZoneRadius * 2f),
				fromLeft: false, centre: new Vector2(-TouchMargin, -TouchMargin), onPressed: null);
			fire.ButtonDown += () => ctx.Player.SetTouchFireHeld(true);
			fire.ButtonUp   += () => ctx.Player.SetTouchFireHeld(false);
			ctx.Player.SetAimJoystick(null);
		}
		else
		{
			var joystickScene = ResourceLoader.Load<PackedScene>("res://Scenes/UI/VirtualJoystick.tscn");
			var aim = joystickScene?.Instantiate<VirtualJoystick>();
			if (aim == null) return;

			aim.AnchorLeft = 1f; aim.AnchorRight = 1f; aim.AnchorTop = 1f; aim.AnchorBottom = 1f;
			aim.Position   = new Vector2(-TouchMargin - aim.Radius, -TouchMargin - aim.Radius);
			cluster.AddChild(aim);
			ctx.Player.SetAimJoystick(aim);
		}
	}

	/// <summary>Called after the aim mode changes (pause menu) to refresh every touch player's cluster.</summary>
	private void RefreshTouchAimClusters()
	{
		foreach (var ctx in _touchPlayers)
			if (IsInstanceValid(ctx.Half) && IsInstanceValid(ctx.Player))
				BuildAimCluster(ctx);
	}

	// Places a touch button by its centre, measured from the bottom-left or bottom-right
	// corner of the player's half.
	private static Button AddTouchButton(Control parent, string text, Vector2 size,
										 bool fromLeft, Vector2 centre, System.Action? onPressed)
	{
		var btn = new Button
		{
			Text        = text,
			Size        = size,
			AnchorLeft  = fromLeft ? 0f : 1f,
			AnchorRight = fromLeft ? 0f : 1f,
			AnchorTop   = 1f, AnchorBottom = 1f,
			Position    = centre - size / 2f,
		};
		btn.AddThemeFontSizeOverride("font_size", 16);
		if (onPressed != null) btn.Pressed += onPressed;
		parent.AddChild(btn);
		return btn;
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
