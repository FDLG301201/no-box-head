using Godot;

namespace NoBoxHead;

public partial class MainMenuUI : Control
{
	public override void _Ready()
	{
		GameManager.Instance?.ResetGame();
		NetworkManager.Instance?.Disconnect();
		BuildUI();
	}

	private void BuildUI()
	{
		// Dark background.
		var bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.1f) };
		bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		// ScrollContainer + CenterContainer instead of a fixed pixel offset: the old
		// Position-based centering assumed a content height that grew (Arena/Skin rows) and
		// started pushing "Settings" off the bottom of the window. This centers when content
		// fits and scrolls instead of clipping when it doesn't — also matters on small mobile
		// screens, where this menu runs too.
		var scroll = new ScrollContainer
		{
			AnchorRight          = 1f, AnchorBottom = 1f,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		AddChild(scroll);

		var hcenter = new CenterContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical   = SizeFlags.ExpandFill,
		};
		scroll.AddChild(hcenter);

		var vbox = new VBoxContainer();
		vbox.CustomMinimumSize = new Vector2(320, 0);
		hcenter.AddChild(vbox);

		AddTitle(vbox, "NO BOX HEAD");

		AddArenaSelector(vbox);
		AddSkinSelector(vbox);

		AddButton(vbox, "Solo Play",   OnSoloPressed);
		AddButton(vbox, "Local Co-op", OnLocalCoopPressed);
		AddButton(vbox, "Host Game",   OnHostPressed);
		AddButton(vbox, "Join Game",   OnJoinPressed);
		AddButton(vbox, "Settings",    OnSettingsPressed);
	}

	// Dropdown to pick the arena. Applies to Solo and Local Co-op; networked games
	// always use the Classic arena so both machines stay in sync.
	private static void AddArenaSelector(Control parent)
	{
		var row = new HBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
		parent.AddChild(row);

		var label = new Label
		{
			Text = "Arena:",
			CustomMinimumSize = new Vector2(90, 0),
			VerticalAlignment = VerticalAlignment.Center
		};
		label.AddThemeFontSizeOverride("font_size", 18);
		row.AddChild(label);

		var opt = new OptionButton
		{
			CustomMinimumSize = new Vector2(230, 44),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		opt.AddThemeFontSizeOverride("font_size", 18);
		foreach (ArenaType t in System.Enum.GetValues<ArenaType>())
			opt.AddItem(ArenaLayouts.DisplayName(t), (int)t);
		opt.Selected = (int)(SettingsManager.Instance?.ArenaType ?? ArenaType.Classic);
		row.AddChild(opt);

		var preview = new ArenaPreview
		{
			CustomMinimumSize = new Vector2(320, 180),
			MouseFilter       = MouseFilterEnum.Ignore,
		};
		parent.AddChild(preview);

		void RefreshPreview()
		{
			var type = SettingsManager.Instance?.ArenaType ?? ArenaType.Classic;
			// Random has no layout until a seed exists, so roll one now and keep it: the whole
			// point of the preview is to show the world that will actually be played, which
			// means the seed has to be decided here rather than at launch.
			if (type == ArenaType.Random) RollArenaSeed();
			preview.SetArena(type, SettingsManager.Instance?.ArenaSeed ?? 0);
		}

		opt.ItemSelected += id =>
		{
			if (SettingsManager.Instance != null)
				SettingsManager.Instance.ArenaType = (ArenaType)(int)id;
			RefreshPreview();
		};

		RefreshPreview();

		parent.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14) });
	}

	private static void RollArenaSeed()
	{
		if (SettingsManager.Instance == null) return;
		var rng = new RandomNumberGenerator();
		rng.Randomize();
		SettingsManager.Instance.ArenaSeed = rng.Seed;
	}

	// Skin picker with a live preview of the character. In co-op / online the other players
	// are automatically offset to neighbouring skins so nobody looks identical.
	private static void AddSkinSelector(Control parent)
	{
		var row = new HBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
		parent.AddChild(row);

		var label = new Label
		{
			Text = "Skin:",
			CustomMinimumSize = new Vector2(90, 0),
			VerticalAlignment = VerticalAlignment.Center
		};
		label.AddThemeFontSizeOverride("font_size", 18);
		row.AddChild(label);

		var preview = new TextureRect
		{
			CustomMinimumSize = new Vector2(44, 44),
			ExpandMode        = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode       = TextureRect.StretchModeEnum.KeepAspectCentered,
		};
		row.AddChild(preview);

		var opt = new OptionButton
		{
			CustomMinimumSize   = new Vector2(186, 44),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		opt.AddThemeFontSizeOverride("font_size", 18);
		for (int i = 0; i < PlayerSkins.Count; i++)
			opt.AddItem(PlayerSkins.Get(i).Name, i);

		int current = SettingsManager.Instance?.SkinIndex ?? 0;
		opt.Selected      = current;
		preview.Texture   = PlayerSkins.LoadTexture(current);

		opt.ItemSelected += id =>
		{
			if (SettingsManager.Instance == null) return;
			SettingsManager.Instance.SkinIndex = (int)id;
			SettingsManager.Instance.SaveSettings();
			preview.Texture = PlayerSkins.LoadTexture((int)id);
		};
		row.AddChild(opt);

		parent.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14) });
	}

	// The world picker already rolled a seed and drew that exact layout, so keep it: re-rolling
	// at launch would drop the player into a different arena than the one they just previewed.
	// A fresh random world still comes from re-picking Random, or from returning to this menu.
	private static void AssignArenaSeedIfRandom()
	{
		if (SettingsManager.Instance?.ArenaType != ArenaType.Random) return;
		if (SettingsManager.Instance.ArenaSeed == 0) RollArenaSeed();
	}

	private static void AddTitle(Control parent, string text)
	{
		var label = new Label
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		label.AddThemeFontSizeOverride("font_size", 40);
		label.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.2f));
		parent.AddChild(label);

		var spacer = new Control { CustomMinimumSize = new Vector2(0, 30) };
		parent.AddChild(spacer);
	}

	private static void AddButton(Control parent, string text, Action pressed)
	{
		var btn = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(320, 50)
		};
		btn.AddThemeFontSizeOverride("font_size", 20);
		btn.Pressed += pressed;
		parent.AddChild(btn);
	}

	private void OnSoloPressed()
	{
		if (SettingsManager.Instance != null)
			SettingsManager.Instance.GameMode = GameMode.SinglePlayer;
		AssignArenaSeedIfRandom();
		GetTree().ChangeSceneToFile("res://Scenes/Arena.tscn");
	}

	private void OnLocalCoopPressed()
	{
		if (SettingsManager.Instance != null)
		{
			SettingsManager.Instance.GameMode = GameMode.LocalCoop;
			// Mouse aim doesn't work with two players; fall back to movement.
			if (SettingsManager.Instance.AimMode == AimMode.Mouse)
				SettingsManager.Instance.AimMode = AimMode.Movement;
		}
		AssignArenaSeedIfRandom();
		GetTree().ChangeSceneToFile("res://Scenes/Arena.tscn");
	}

	private void OnHostPressed()
	{
		EnterLobby(asHost: true);
	}

	private void OnJoinPressed()
	{
		EnterLobby(asHost: false);
	}

	// GameMode is session state, so without setting it here a networked game inherited whatever
	// was played last — and coming from Local Co-op that made the online client build a split
	// screen for a second player who only exists on the other machine.
	private void EnterLobby(bool asHost)
	{
		LobbyMode.IsHost = asHost;
		if (SettingsManager.Instance != null)
			SettingsManager.Instance.GameMode = GameMode.Online;
		GetTree().ChangeSceneToFile("res://Scenes/Lobby.tscn");
	}

	private void OnSettingsPressed() =>
		GetTree().ChangeSceneToFile("res://Scenes/Settings.tscn");
}
