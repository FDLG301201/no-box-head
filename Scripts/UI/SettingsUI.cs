using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

public partial class SettingsUI : Control
{
    private Button? _sharedBtn;
    private Button? _splitBtn;
    private Button? _aimMoveBtn;
    private Button? _aimMouseBtn;
    private Button? _aimAutoBtn;

    // Key-rebinding state: while an action is "listening", the next key press is captured.
    private readonly Dictionary<string, Button> _bindButtons = new();
    private string? _listeningAction;
    private Label?  _bindHint;

    public override void _Ready() => BuildUI();

    private void BuildUI()
    {
        var bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.1f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Root split: scrollable settings on top, fixed Back button at the bottom.
        var root = new VBoxContainer
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft  = 40f, OffsetRight  = -40f,
            OffsetTop   = 20f, OffsetBottom = -20f,
        };
        root.AddThemeConstantOverride("separation", 10);
        AddChild(root);

        var title = new Label { Text = "Settings", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        root.AddChild(title);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        root.AddChild(scroll);

        var vbox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        BuildCameraSection(vbox);
        BuildAudioSection(vbox);
        BuildAimSection(vbox);
        BuildControlsSection(vbox);

        UpdateButtonStates();

        var backBtn = new Button { Text = "Back", CustomMinimumSize = new Vector2(0, 50) };
        backBtn.AddThemeFontSizeOverride("font_size", 20);
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        root.AddChild(backBtn);
    }

    // ── Sections ──────────────────────────────────────────────────────────────

    private void BuildCameraSection(VBoxContainer vbox)
    {
        var camLabel = new Label { Text = "Camera Mode" };
        camLabel.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(camLabel);

        var camRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 50) };
        vbox.AddChild(camRow);

        _sharedBtn = MakeToggleBtn("Shared Camera");
        _sharedBtn.Pressed += () => SetCameraMode(CameraMode.Shared);
        camRow.AddChild(_sharedBtn);

        _splitBtn = MakeToggleBtn("Split Screen");
        _splitBtn.Pressed += () => SetCameraMode(CameraMode.SplitScreen);
        camRow.AddChild(_splitBtn);

        vbox.AddChild(Spacer(16));
    }

    private void BuildAudioSection(VBoxContainer vbox)
    {
        var header = new Label { Text = "Sound Effects" };
        header.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(header);

        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 44) };
        vbox.AddChild(row);

        var slider = new HSlider
        {
            MinValue            = 0,
            MaxValue            = 1,
            Step                = 0.05,
            Value               = SettingsManager.Instance?.SfxVolume ?? 0.8f,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical   = SizeFlags.ShrinkCenter,
        };
        row.AddChild(slider);

        var readout = new Label
        {
            CustomMinimumSize = new Vector2(60, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        readout.AddThemeFontSizeOverride("font_size", 15);
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
        // Preview the new level so the slider gives immediate feedback.
        slider.DragEnded += _ => AudioManager.Instance?.Play(AudioManager.PickupAmmo, 0.7f);

        vbox.AddChild(Spacer(16));
    }

    private void BuildAimSection(VBoxContainer vbox)
    {
        var aimLabel = new Label { Text = "Aim Mode" };
        aimLabel.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(aimLabel);

        var aimDesc = new Label
        {
            Text = "Movement: aims toward the direction you walk\n" +
                   "Mouse: aims toward the cursor (single player only)\n" +
                   "Auto-Aim: locks onto the nearest enemy",
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        aimDesc.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        aimDesc.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(aimDesc);
        vbox.AddChild(Spacer(6));

        var aimRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 50) };
        vbox.AddChild(aimRow);

        _aimMoveBtn  = MakeToggleBtn("Movement");
        _aimMouseBtn = MakeToggleBtn("Mouse");
        _aimAutoBtn  = MakeToggleBtn("Auto-Aim");

        _aimMoveBtn.Pressed  += () => SetAimMode(AimMode.Movement);
        _aimMouseBtn.Pressed += () => SetAimMode(AimMode.Mouse);
        _aimAutoBtn.Pressed  += () => SetAimMode(AimMode.AutoAim);

        aimRow.AddChild(_aimMoveBtn);
        aimRow.AddChild(_aimMouseBtn);
        aimRow.AddChild(_aimAutoBtn);

        vbox.AddChild(Spacer(16));
    }

    private void BuildControlsSection(VBoxContainer vbox)
    {
        var header = new Label { Text = "Controls (Player 1)" };
        header.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(header);

        _bindHint = new Label
        {
            Text         = "Click a key to rebind it. Escape cancels.",
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _bindHint.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        _bindHint.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(_bindHint);
        vbox.AddChild(Spacer(6));

        foreach (var (action, label) in SettingsManager.BindableActions)
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 40) };
            vbox.AddChild(row);

            var name = new Label
            {
                Text                = label,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            name.AddThemeFontSizeOverride("font_size", 15);
            row.AddChild(name);

            var keyBtn = new Button
            {
                Text              = KeyName(action),
                CustomMinimumSize = new Vector2(160, 36),
            };
            keyBtn.AddThemeFontSizeOverride("font_size", 15);
            var captured = action;
            keyBtn.Pressed += () => StartListening(captured);
            _bindButtons[action] = keyBtn;
            row.AddChild(keyBtn);
        }

        vbox.AddChild(Spacer(10));

        var resetBtn = new Button { Text = "Reset to Defaults", CustomMinimumSize = new Vector2(0, 44) };
        resetBtn.AddThemeFontSizeOverride("font_size", 16);
        resetBtn.Pressed += () =>
        {
            SettingsManager.Instance?.ResetBindings();
            RefreshBindButtons();
        };
        vbox.AddChild(resetBtn);
        vbox.AddChild(Spacer(16));
    }

    // ── Key rebinding ─────────────────────────────────────────────────────────

    private void StartListening(string action)
    {
        _listeningAction = action;
        if (_bindButtons.TryGetValue(action, out var btn)) btn.Text = "Press a key…";
        if (_bindHint != null) _bindHint.Text = "Listening… press any key (Escape cancels).";
    }

    public override void _Input(InputEvent ev)
    {
        if (_listeningAction == null) return;
        if (ev is not InputEventKey { Pressed: true, Echo: false } key) return;

        var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        var action = _listeningAction;
        _listeningAction = null;
        GetViewport().SetInputAsHandled();

        if (code == Key.Escape)
        {
            SetHint("Rebind cancelled.");
            RefreshBindButtons();
            return;
        }

        // Clear the key off whatever else was using it so two actions can't share a bind.
        var conflict = SettingsManager.Instance?.FindConflict(action, code);
        SettingsManager.Instance?.SetBinding(action, code);
        RefreshBindButtons();

        SetHint(conflict != null
            ? $"Bound. Note: this key was also on \"{LabelFor(conflict)}\" — rebind that one too."
            : "Click a key to rebind it. Escape cancels.");
    }

    private void RefreshBindButtons()
    {
        foreach (var (action, btn) in _bindButtons)
            if (IsInstanceValid(btn)) btn.Text = KeyName(action);
    }

    private static string KeyName(string action)
    {
        var key = SettingsManager.Instance?.GetBinding(action) ?? Key.None;
        return key == Key.None ? "—" : OS.GetKeycodeString(key);
    }

    private static string LabelFor(string action)
    {
        foreach (var (a, label) in SettingsManager.BindableActions)
            if (a == action) return label;
        return action;
    }

    private void SetHint(string text)
    {
        if (_bindHint != null) _bindHint.Text = text;
    }

    // ── Camera / Aim ──────────────────────────────────────────────────────────

    private void SetCameraMode(CameraMode mode)
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.CameraMode = mode;
            SettingsManager.Instance.SaveSettings();
        }
        UpdateButtonStates();
    }

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

    private static Button MakeToggleBtn(string text) => new()
    {
        Text                = text,
        CustomMinimumSize   = new Vector2(0, 50),
        ToggleMode          = true,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };

    private static Control Spacer(int height) => new() { CustomMinimumSize = new Vector2(0, height) };
}
