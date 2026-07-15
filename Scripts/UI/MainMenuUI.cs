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

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		vbox.CustomMinimumSize = new Vector2(320, 0);
		vbox.Position -= new Vector2(160, 120);
		AddChild(vbox);

		AddTitle(vbox, "NO BOX HEAD");

		AddButton(vbox, "Solo Play",   OnSoloPressed);
		AddButton(vbox, "Local Co-op", OnLocalCoopPressed);
		AddButton(vbox, "Host Game",   OnHostPressed);
		AddButton(vbox, "Join Game",   OnJoinPressed);
		AddButton(vbox, "Settings",    OnSettingsPressed);
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
		GetTree().ChangeSceneToFile("res://Scenes/Arena.tscn");
	}

	private void OnHostPressed()
	{
		LobbyMode.IsHost = true;
		GetTree().ChangeSceneToFile("res://Scenes/Lobby.tscn");
	}

	private void OnJoinPressed()
	{
		LobbyMode.IsHost = false;
		GetTree().ChangeSceneToFile("res://Scenes/Lobby.tscn");
	}

	private void OnSettingsPressed() =>
		GetTree().ChangeSceneToFile("res://Scenes/Settings.tscn");
}
