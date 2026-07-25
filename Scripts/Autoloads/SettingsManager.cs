using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

public enum CameraMode { Shared, SplitScreen }
public enum AimMode    { Movement, Mouse, AutoAim }
public enum GameMode   { SinglePlayer, LocalCoop }

public partial class SettingsManager : Node
{
    public static SettingsManager Instance { get; private set; } = null!;

    public CameraMode CameraMode { get; set; } = CameraMode.Shared;
    public AimMode    AimMode    { get; set; } = AimMode.Movement;
    // 0 = muted, 1 = full. Persisted.
    public float      SfxVolume  { get; set; } = 0.8f;
    // Session-only, not persisted to disk.
    public GameMode   GameMode   { get; set; } = GameMode.SinglePlayer;
    public ArenaType  ArenaType  { get; set; } = ArenaType.Classic;
    // Seed for the Random arena; assigned when a game starts so the whole session matches.
    public ulong      ArenaSeed  { get; set; }

    /// <summary>
    /// Player 1 actions exposed on the Settings screen for rebinding. Player 2 keeps its
    /// fixed arrow-keys + numpad layout (its inputs are read as physical keycodes so they
    /// work regardless of Num Lock, see Player._Input).
    /// </summary>
    public static readonly (string Action, string Label)[] BindableActions =
    {
        ("move_up",            "Move Up"),
        ("move_down",          "Move Down"),
        ("move_left",          "Move Left"),
        ("move_right",         "Move Right"),
        ("shoot",              "Shoot"),
        ("switch_weapon",      "Next Weapon"),
        ("switch_weapon_prev", "Prev Weapon"),
        ("knife",              "Knife"),
        ("pause",              "Pause"),
    };

    private const string SettingsPath = "user://settings.cfg";
    private readonly ConfigFile _config = new();

    // Keys defined in project.godot, captured before any override is applied so
    // "Reset to defaults" can restore them without reloading the project.
    private readonly Dictionary<string, Key> _defaultBindings = new();
    private readonly Dictionary<string, Key> _bindings        = new();

    public override void _Ready()
    {
        Instance = this;
        CaptureDefaultBindings();
        LoadSettings();
        ApplyAllBindings();
    }

    // ── Settings persistence ──────────────────────────────────────────────────

    public void SaveSettings()
    {
        _config.SetValue("camera",   "mode",       (int)CameraMode);
        _config.SetValue("controls", "aim_mode",   (int)AimMode);
        _config.SetValue("audio",    "sfx_volume", SfxVolume);
        foreach (var (action, key) in _bindings)
            _config.SetValue("bindings", action, (int)key);
        _config.Save(SettingsPath);
    }

    private void LoadSettings()
    {
        if (_config.Load(SettingsPath) != Error.Ok) return;
        CameraMode = (CameraMode)(int)_config.GetValue("camera",   "mode",     (int)CameraMode.Shared);
        AimMode    = (AimMode)   (int)_config.GetValue("controls", "aim_mode", (int)AimMode.Movement);
        SfxVolume  = (float)     _config.GetValue("audio", "sfx_volume", 0.8f);

        foreach (var (action, _) in BindableActions)
        {
            var stored = _config.GetValue("bindings", action, (int)Key.None);
            var key    = (Key)(int)stored;
            if (key != Key.None) _bindings[action] = key;
        }
    }

    // ── Key bindings ──────────────────────────────────────────────────────────

    public Key GetBinding(string action) =>
        _bindings.TryGetValue(action, out var key) ? key
        : _defaultBindings.GetValueOrDefault(action, Key.None);

    public void SetBinding(string action, Key key)
    {
        _bindings[action] = key;
        ApplyBinding(action, key);
        SaveSettings();
    }

    public void ResetBindings()
    {
        _bindings.Clear();
        foreach (var (action, _) in BindableActions)
            _config.SetValue("bindings", action, (int)Key.None);
        ApplyAllBindings();
        SaveSettings();
    }

    /// <summary>True if another action already uses this key (so the UI can warn/swap).</summary>
    public string? FindConflict(string action, Key key)
    {
        foreach (var (other, _) in BindableActions)
            if (other != action && GetBinding(other) == key) return other;
        return null;
    }

    private void CaptureDefaultBindings()
    {
        foreach (var (action, _) in BindableActions)
        {
            if (!InputMap.HasAction(action)) continue;
            foreach (var ev in InputMap.ActionGetEvents(action))
            {
                if (ev is not InputEventKey k) continue;
                _defaultBindings[action] = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
                break;
            }
        }
    }

    private void ApplyAllBindings()
    {
        foreach (var (action, _) in BindableActions)
            ApplyBinding(action, GetBinding(action));
    }

    private static void ApplyBinding(string action, Key key)
    {
        if (!InputMap.HasAction(action) || key == Key.None) return;
        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        // Escape always stays available as a pause shortcut, whatever the custom bind is.
        if (action == "pause" && key != Key.Escape)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = Key.Escape });
    }
}
