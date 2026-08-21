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
    private Button? _bloodOnBtn;
    private Button? _bloodOffBtn;
    private Button[]? _bloodColorBtns;

    // Key-rebinding state: while an action is "listening", the next key press is captured.
    private readonly Dictionary<string, Button> _bindButtons = new();
    private string? _listeningAction;
    private Label?  _bindHint;

    public override void _Ready() => BuildUI();

    private void BuildUI()
    {
        var bg = new ColorRect
        {
            Color       = new Color(0.08f, 0.08f, 0.1f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
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

        var title = MakeLabel("Settings", HorizontalAlignment.Center);
        title.AddThemeFontSizeOverride("font_size", 30);
        root.AddChild(title);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical    = SizeFlags.ExpandFill,
            SizeFlagsHorizontal  = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            // Keeps drag/wheel scrolling functional but hides the visual scrollbar strip —
            // was showing as an ugly permanent sidebar down the right edge on mobile.
            VerticalScrollMode   = ScrollContainer.ScrollMode.ShowNever,
        };
        root.AddChild(scroll);

        var vbox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        // Phones get audio and aim only. Camera mode is pointless there — a shared camera is
        // unplayable on a handheld, so local co-op is always split screen (forced in
        // SettingsManager), and key rebinding has no keyboard to rebind. Both sections would
        // just be dead controls eating room on a small screen.
        if (!Platform.IsMobile) BuildCameraSection(vbox);
        BuildAudioSection(vbox);
        BuildMusicSection(vbox);
        BuildBloodSection(vbox);
        BuildAimSection(vbox);
        if (!Platform.IsMobile) BuildControlsSection(vbox);

        UpdateButtonStates();

        var backBtn = new Button
        {
            Text = "Back", CustomMinimumSize = new Vector2(0, 50),
            ThemeTypeVariation = "ButtonDanger",
        };
        backBtn.AddThemeFontSizeOverride("font_size", 20);
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        root.AddChild(backBtn);
    }

    // ── Sections ──────────────────────────────────────────────────────────────

    private void BuildCameraSection(VBoxContainer vbox)
    {
        var camLabel = MakeLabel("Camera Mode");
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
        var header = MakeLabel("Sound Effects");
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

        var readout = MakeLabel("");
        readout.CustomMinimumSize = new Vector2(60, 0);
        readout.VerticalAlignment = VerticalAlignment.Center;
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

    /// <summary>
    /// Music volume, plus a track picker once there is more than one track to pick from —
    /// showing a one-item selector would just be noise.
    /// </summary>
    private void BuildMusicSection(VBoxContainer vbox)
    {
        var header = MakeLabel("Music");
        header.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(header);

        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 44) };
        vbox.AddChild(row);

        var slider = new HSlider
        {
            MinValue            = 0,
            MaxValue            = 1,
            Step                = 0.05,
            Value               = SettingsManager.Instance?.MusicVolume ?? 0.5f,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical   = SizeFlags.ShrinkCenter,
        };
        row.AddChild(slider);

        var readout = MakeLabel("");
        readout.CustomMinimumSize = new Vector2(60, 0);
        readout.VerticalAlignment = VerticalAlignment.Center;
        readout.AddThemeFontSizeOverride("font_size", 15);
        row.AddChild(readout);

        void Refresh(double v) => readout.Text = v <= 0.001 ? "Off" : $"{Mathf.RoundToInt((float)v * 100)}%";
        Refresh(slider.Value);

        slider.ValueChanged += v =>
        {
            if (SettingsManager.Instance == null) return;
            SettingsManager.Instance.MusicVolume = (float)v;
            SettingsManager.Instance.SaveSettings();
            // Applied live so the slider is audible while you drag it.
            MusicManager.Instance?.ApplyVolume();
            Refresh(v);
        };

        if (MusicManager.Tracks.Length > 1)
        {
            var picker = new OptionButton { CustomMinimumSize = new Vector2(0, 44) };
            picker.AddThemeFontSizeOverride("font_size", 16);
            foreach (var (_, label, _) in MusicManager.Tracks) picker.AddItem(label);
            picker.Selected = Mathf.Clamp(SettingsManager.Instance?.MusicTrackIndex ?? 0,
                                          0, MusicManager.Tracks.Length - 1);
            picker.ItemSelected += id =>
            {
                if (SettingsManager.Instance == null) return;
                SettingsManager.Instance.MusicTrackIndex = (int)id;
                SettingsManager.Instance.SaveSettings();
                MusicManager.Instance?.PlaySelected();
            };
            vbox.AddChild(picker);
        }

        vbox.AddChild(Spacer(16));
    }

    /// <summary>
    /// Blood on/off plus a colour preset. Deliberately NOT gated behind !Platform.IsMobile like
    /// the camera and rebinding sections: this is the one setting a parent may need to reach on
    /// a phone, which is the platform the game ships on.
    /// </summary>
    private void BuildBloodSection(VBoxContainer vbox)
    {
        var header = MakeLabel("Blood");
        header.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(header);

        var toggleRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 50) };
        vbox.AddChild(toggleRow);

        _bloodOnBtn  = MakeToggleBtn("On");
        _bloodOffBtn = MakeToggleBtn("Off");
        _bloodOnBtn.Pressed  += () => SetBloodEnabled(true);
        _bloodOffBtn.Pressed += () => SetBloodEnabled(false);
        toggleRow.AddChild(_bloodOnBtn);
        toggleRow.AddChild(_bloodOffBtn);

        var colourRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 50) };
        vbox.AddChild(colourRow);

        _bloodColorBtns = new Button[BloodPalettes.Names.Length];
        for (int i = 0; i < BloodPalettes.Names.Length; i++)
        {
            int index = i; // capture per iteration, not the shared loop variable
            var btn = MakeToggleBtn(BloodPalettes.Names[i]);
            btn.Pressed += () => SetBloodColor(index);
            _bloodColorBtns[i] = btn;
            colourRow.AddChild(btn);
        }

        vbox.AddChild(Spacer(16));
    }

    private void SetBloodEnabled(bool enabled)
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.BloodEnabled = enabled;
            SettingsManager.Instance.SaveSettings();
        }
        UpdateButtonStates();
    }

    private void SetBloodColor(int index)
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.BloodColorIndex = index;
            SettingsManager.Instance.SaveSettings();
        }
        UpdateButtonStates();
    }

    private void BuildAimSection(VBoxContainer vbox)
    {
        var aimLabel = MakeLabel("Aim Mode");
        aimLabel.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(aimLabel);

        var aimDesc = MakeLabel(
            "Movement: aims toward the direction you walk\n" +
            "Mouse: aims toward the cursor (single player only)\n" +
            "Auto-Aim: locks onto the nearest enemy");
        aimDesc.AutowrapMode = TextServer.AutowrapMode.Word;
        aimDesc.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        aimDesc.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(aimDesc);
        vbox.AddChild(Spacer(6));

        var aimRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 50) };
        vbox.AddChild(aimRow);

        _aimMoveBtn = MakeToggleBtn("Movement");
        _aimAutoBtn = MakeToggleBtn("Auto-Aim");

        _aimMoveBtn.Pressed += () => SetAimMode(AimMode.Movement);
        _aimAutoBtn.Pressed += () => SetAimMode(AimMode.AutoAim);

        aimRow.AddChild(_aimMoveBtn);

        // A touch device has no cursor, so mouse aim is not merely unavailable there — it is
        // meaningless. It used to be added and greyed out, which left an unusable button
        // eating a third of the row on the smallest screens.
        if (!Platform.IsMobile)
        {
            _aimMouseBtn = MakeToggleBtn("Mouse");
            _aimMouseBtn.Pressed += () => SetAimMode(AimMode.Mouse);
            aimRow.AddChild(_aimMouseBtn);
        }

        aimRow.AddChild(_aimAutoBtn);

        vbox.AddChild(Spacer(16));
    }

    private void BuildControlsSection(VBoxContainer vbox)
    {
        BuildControlsRows(vbox, "Controls (Player 1)", SettingsManager.BindableActions);
        BuildControlsRows(vbox, "Controls (Player 2)", SettingsManager.BindableActionsP2);

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

    /// <summary>
    /// One player's rebinding rows. Both players share _bindButtons/RefreshBindButtons — their
    /// action names never collide ("move_up" vs "move_up_p2") so one dictionary keyed by action
    /// covers both without any extra per-player bookkeeping.
    /// </summary>
    private void BuildControlsRows(VBoxContainer vbox, string headerText, (string Action, string Label)[] actions)
    {
        var header = MakeLabel(headerText);
        header.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(header);

        if (_bindHint == null)
        {
            _bindHint = MakeLabel("Click a key to rebind it. Escape cancels.");
            _bindHint.AutowrapMode = TextServer.AutowrapMode.Word;
            _bindHint.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            _bindHint.AddThemeFontSizeOverride("font_size", 13);
            vbox.AddChild(_bindHint);
            vbox.AddChild(Spacer(6));
        }

        foreach (var (action, label) in actions)
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 40) };
            vbox.AddChild(row);

            var name = MakeLabel(label);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.VerticalAlignment   = VerticalAlignment.Center;
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
        foreach (var (a, label) in SettingsManager.BindableActionsP2)
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

        bool bloodOn = SettingsManager.Instance?.BloodEnabled ?? true;
        if (_bloodOnBtn  != null) _bloodOnBtn.ButtonPressed  = bloodOn;
        if (_bloodOffBtn != null) _bloodOffBtn.ButtonPressed = !bloodOn;

        int bloodColor = SettingsManager.Instance?.BloodColorIndex ?? 0;
        if (_bloodColorBtns != null)
            for (int i = 0; i < _bloodColorBtns.Length; i++)
            {
                _bloodColorBtns[i].ButtonPressed = i == bloodColor;
                // Colour only matters while blood is drawn at all.
                _bloodColorBtns[i].Disabled      = !bloodOn;
            }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Button MakeToggleBtn(string text) => new()
    {
        Text                = text,
        CustomMinimumSize   = new Vector2(0, 50),
        ToggleMode          = true,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };

    private static Control Spacer(int height) =>
        new() { CustomMinimumSize = new Vector2(0, height), MouseFilter = Control.MouseFilterEnum.Ignore };

    // Purely decorative text (headers, descriptions, tags) must ignore mouse/touch input.
    // Control's default filter is Stop, which — since Godot resolves an input event against
    // whatever Control is directly under the finger — meant a drag gesture starting on top of
    // one of these labels (very likely: this screen is mostly text) never reached the
    // ScrollContainer to be recognised as a scroll at all. Interactive widgets (Button,
    // HSlider, LineEdit) are left with their default Stop filter so they still capture clicks.
    private static Label MakeLabel(string text, HorizontalAlignment align = HorizontalAlignment.Left) => new()
    {
        Text                = text,
        HorizontalAlignment = align,
        MouseFilter         = Control.MouseFilterEnum.Ignore,
    };
}
