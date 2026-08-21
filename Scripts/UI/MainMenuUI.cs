using Godot;

namespace NoBoxHead;

public partial class MainMenuUI : Control
{
	// Deliberately tight vertical rhythm. This menu has to clear a title, two pickers, an arena
	// preview and five buttons, and it also runs in small windows and on phones. At the earlier
	// sizing the content stood 691px tall, so on a 531px window the bottom buttons fell off the
	// screen entirely — these are what keep the whole menu on one screen.
	private const int RowGap       = 10;
	private const int ButtonHeight = 42;

	public override void _Ready()
	{
		GameManager.Instance?.ResetGame();
		NetworkManager.Instance?.Disconnect();
		BuildUI();
		// Starting it here rather than in the Arena means the track keeps running across the
		// menu → game → back-to-menu transitions instead of restarting on every scene change:
		// MusicManager.Play() is a no-op when the requested track is already the one playing.
		MusicManager.Instance?.PlaySelected();
	}

	private void BuildUI()
	{
		// Dark background.
		var bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.1f) };
		bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		// Added here — after the background, before the menu — because CanvasItem siblings draw
		// in child order, so this is what puts the horde behind the title and buttons rather than
		// over them. It is a Node2D, so it adds nothing to the content height the constants above
		// exist to protect.
		AddChild(new MenuHorde());

		// A ScrollContainer so the menu survives content taller than the window (it grew with
		// the Arena/Skin rows and again with the arena preview) and small mobile screens.
		//
		// The content is centred horizontally only. A CenterContainer used to do both axes,
		// which quietly breaks once the content outgrows the viewport: it centres the child,
		// so the overflow goes ABOVE the scroll origin as well as below, and the top — title,
		// arena picker — ends up clipped where scrolling cannot reach it. Top-aligned content
		// always starts at a scrollable position, whatever its height.
		var scroll = new ScrollContainer
		{
			AnchorRight          = 1f, AnchorBottom = 1f,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		AddChild(scroll);

		// ScrollContainer places its child at the origin and only stretches it when the child
		// asks to expand, so ShrinkCenter on the child alone does nothing and the menu hugs the
		// left edge. This full-width column is what expands; a BoxContainer DOES honour its
		// children's cross-axis flags, so the inner column below centres properly inside it.
		var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		column.AddThemeConstantOverride("separation", 2);
		scroll.AddChild(column);

		// Breathing room so the title is not flush against the top edge.
		column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

		var vbox = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			CustomMinimumSize   = new Vector2(320, 0),
		};
		vbox.AddThemeConstantOverride("separation", 2);
		column.AddChild(vbox);

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

		// 16:9 like the arena itself, but kept small: this menu also runs on short windows and
		// phone screens, where every row it pushes down is a button the player cannot see.
		var preview = new ArenaPreview
		{
			CustomMinimumSize = new Vector2(192, 108),
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
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

		parent.AddChild(new Control { CustomMinimumSize = new Vector2(0, RowGap) });
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

		parent.AddChild(new Control { CustomMinimumSize = new Vector2(0, RowGap) });
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
		label.AddThemeFontSizeOverride("font_size", 32);
		label.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.2f));
		parent.AddChild(label);

		parent.AddChild(new Control { CustomMinimumSize = new Vector2(0, RowGap) });
	}

	private static void AddButton(Control parent, string text, Action pressed)
	{
		var btn = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(320, ButtonHeight)
		};
		btn.AddThemeFontSizeOverride("font_size", 19);
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
