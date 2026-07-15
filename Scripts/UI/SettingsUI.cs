using Godot;

namespace NoBoxHead;

public partial class SettingsUI : Control
{
    private Button? _sharedBtn;
    private Button? _splitBtn;

    public override void _Ready() => BuildUI();

    private void BuildUI()
    {
        var bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.1f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        vbox.CustomMinimumSize = new Vector2(360, 0);
        vbox.Position -= new Vector2(180, 100);
        AddChild(vbox);

        var title = new Label { Text = "Settings", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        vbox.AddChild(title);
        vbox.AddChild(Spacer(20));

        var camLabel = new Label { Text = "Camera Mode" };
        camLabel.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(camLabel);

        // Camera mode toggle buttons.
        var hbox = new HBoxContainer { CustomMinimumSize = new Vector2(0, 50) };
        vbox.AddChild(hbox);

        _sharedBtn = new Button { Text = "Shared Camera", CustomMinimumSize = new Vector2(170, 50), ToggleMode = true };
        _sharedBtn.AddThemeFontSizeOverride("font_size", 16);
        _sharedBtn.Pressed += () => SetCameraMode(CameraMode.Shared);
        hbox.AddChild(_sharedBtn);

        _splitBtn = new Button { Text = "Split Screen", CustomMinimumSize = new Vector2(170, 50), ToggleMode = true };
        _splitBtn.AddThemeFontSizeOverride("font_size", 16);
        _splitBtn.Pressed += () => SetCameraMode(CameraMode.SplitScreen);
        hbox.AddChild(_splitBtn);

        UpdateButtonStates();
        vbox.AddChild(Spacer(30));

        var backBtn = new Button { Text = "Back", CustomMinimumSize = new Vector2(360, 50) };
        backBtn.AddThemeFontSizeOverride("font_size", 20);
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        vbox.AddChild(backBtn);
    }

    private void SetCameraMode(CameraMode mode)
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.CameraMode = mode;
            SettingsManager.Instance.SaveSettings();
        }
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        var current = SettingsManager.Instance?.CameraMode ?? CameraMode.Shared;
        if (_sharedBtn != null) _sharedBtn.ButtonPressed = current == CameraMode.Shared;
        if (_splitBtn != null) _splitBtn.ButtonPressed = current == CameraMode.SplitScreen;
    }

    private static Control Spacer(int height) => new Control { CustomMinimumSize = new Vector2(0, height) };
}
