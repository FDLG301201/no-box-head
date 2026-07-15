using Godot;

namespace NoBoxHead;

public partial class SettingsUI : Control
{
    private Button? _sharedBtn;
    private Button? _splitBtn;
    private Button? _aimMoveBtn;
    private Button? _aimMouseBtn;
    private Button? _aimAutoBtn;

    public override void _Ready() => BuildUI();

    private void BuildUI()
    {
        var bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.1f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        vbox.CustomMinimumSize = new Vector2(380, 0);
        vbox.Position -= new Vector2(190, 160);
        AddChild(vbox);

        var title = new Label { Text = "Settings", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        vbox.AddChild(title);
        vbox.AddChild(Spacer(20));

        // ── Camera Mode ───────────────────────────────────────────────────────
        var camLabel = new Label { Text = "Camera Mode" };
        camLabel.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(camLabel);

        var camRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 50) };
        vbox.AddChild(camRow);

        _sharedBtn = MakeToggleBtn("Shared Camera", 185);
        _sharedBtn.Pressed += () => SetCameraMode(CameraMode.Shared);
        camRow.AddChild(_sharedBtn);

        _splitBtn = MakeToggleBtn("Split Screen", 185);
        _splitBtn.Pressed += () => SetCameraMode(CameraMode.SplitScreen);
        camRow.AddChild(_splitBtn);

        vbox.AddChild(Spacer(20));

        // ── Aim Mode ──────────────────────────────────────────────────────────
        var aimLabel = new Label { Text = "Aim Mode" };
        aimLabel.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(aimLabel);

        var aimDesc = new Label
        {
            Text = "Movement: rotates toward WASD direction\n" +
                   "Mouse: rotates toward cursor\n" +
                   "Auto-Aim: locks onto nearest enemy",
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        aimDesc.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        aimDesc.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(aimDesc);
        vbox.AddChild(Spacer(6));

        var aimRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 50) };
        vbox.AddChild(aimRow);

        _aimMoveBtn  = MakeToggleBtn("Movement", 122);
        _aimMouseBtn = MakeToggleBtn("Mouse",    122);
        _aimAutoBtn  = MakeToggleBtn("Auto-Aim", 122);

        _aimMoveBtn.Pressed  += () => SetAimMode(AimMode.Movement);
        _aimMouseBtn.Pressed += () => SetAimMode(AimMode.Mouse);
        _aimAutoBtn.Pressed  += () => SetAimMode(AimMode.AutoAim);

        aimRow.AddChild(_aimMoveBtn);
        aimRow.AddChild(_aimMouseBtn);
        aimRow.AddChild(_aimAutoBtn);

        UpdateButtonStates();
        vbox.AddChild(Spacer(30));

        var backBtn = new Button { Text = "Back", CustomMinimumSize = new Vector2(380, 50) };
        backBtn.AddThemeFontSizeOverride("font_size", 20);
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        vbox.AddChild(backBtn);
    }

    // ── Camera ────────────────────────────────────────────────────────────────

    private void SetCameraMode(CameraMode mode)
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.CameraMode = mode;
            SettingsManager.Instance.SaveSettings();
        }
        UpdateButtonStates();
    }

    // ── Aim ───────────────────────────────────────────────────────────────────

    private void SetAimMode(AimMode mode)
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.AimMode = mode;
            SettingsManager.Instance.SaveSettings();
        }
        UpdateButtonStates();
    }

    // ── State sync ────────────────────────────────────────────────────────────

    private void UpdateButtonStates()
    {
        var cam = SettingsManager.Instance?.CameraMode ?? CameraMode.Shared;
        if (_sharedBtn != null) _sharedBtn.ButtonPressed = cam == CameraMode.Shared;
        if (_splitBtn  != null) _splitBtn.ButtonPressed  = cam == CameraMode.SplitScreen;

        var aim = SettingsManager.Instance?.AimMode ?? AimMode.Movement;
        if (_aimMoveBtn  != null) _aimMoveBtn.ButtonPressed  = aim == AimMode.Movement;
        if (_aimMouseBtn != null) _aimMouseBtn.ButtonPressed = aim == AimMode.Mouse;
        if (_aimAutoBtn  != null) _aimAutoBtn.ButtonPressed  = aim == AimMode.AutoAim;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Button MakeToggleBtn(string text, int width) => new Button
    {
        Text              = text,
        CustomMinimumSize = new Vector2(width, 50),
        ToggleMode        = true,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
    };

    private static Control Spacer(int height) => new Control { CustomMinimumSize = new Vector2(0, height) };
}
