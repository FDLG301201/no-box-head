using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

public enum CameraMode { Shared, SplitScreen }
public enum AimMode    { Movement, Mouse, AutoAim }
// Online is a distinct mode, not "co-op that happens to be networked": every peer renders its
// own full screen. Without this value a networked session kept whatever mode was set last, so
// after one local co-op game the online client still took every LocalCoop branch — split
// screen, the P2 HUD panel, co-op joysticks, tabletop rotation — none of which apply.
public enum GameMode   { SinglePlayer, LocalCoop, Online }

public partial class SettingsManager : Node
{
    public static SettingsManager Instance { get; private set; } = null!;

    /// <summary>
    /// Camera layout for local co-op. On a handheld this is always SplitScreen and the stored
    /// preference is ignored: two people cannot share one phone-sized view, and the settings
    /// screen hides the picker there, so nothing would ever set it back. Desktop keeps the
    /// saved choice.
    /// </summary>
    public CameraMode CameraMode
    {
        get => Platform.IsMobile ? CameraMode.SplitScreen : _cameraMode;
        set => _cameraMode = value;
    }
    private CameraMode _cameraMode = CameraMode.Shared;
    public AimMode    AimMode    { get; set; } = AimMode.Movement;
    // 0 = muted, 1 = full. Persisted.
    public float      SfxVolume  { get; set; } = 0.8f;
    // Music sits under the effects by default: it should not bury the shots that tell you
    // an enemy is behind you. Persisted.
    public float      MusicVolume { get; set; } = 0.5f;
    // Index into MusicManager.Tracks. Persisted.
    public int        MusicTrackIndex { get; set; }
    // Index into PlayerSkins.All. Persisted.
    public int        SkinIndex  { get; set; }
    // Whether blood decals/particles render at all. Persisted.
    public bool       BloodEnabled   { get; set; } = true;
    // Index into BloodPalettes.Names. Persisted.
    public int        BloodColorIndex { get; set; }
    // Session-only, not persisted to disk.
    public GameMode   GameMode   { get; set; } = GameMode.SinglePlayer;
    public ArenaType  ArenaType  { get; set; } = ArenaType.Classic;
    // Seed for the Random arena; assigned when a game starts so the whole session matches.
    public ulong      ArenaSeed  { get; set; }

    /// <summary>Player 1 actions exposed on the Settings screen for rebinding.</summary>
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

    /// <summary>
    /// Player 2's actions, rebindable the same way as player 1's. Mirrors BindableActions but
    /// targets the "_p2"-suffixed actions in the input map (see Player.UsesSecondaryBindings /
    /// Player.A) — those default to arrows + numpad, read as physical keycodes via the action
    /// map itself so they still work regardless of Num Lock. No "pause" entry: pause is shared
    /// between both players, not per-player.
    /// </summary>
    public static readonly (string Action, string Label)[] BindableActionsP2 =
    {
        ("move_up_p2",            "Move Up"),
        ("move_down_p2",          "Move Down"),
        ("move_left_p2",          "Move Left"),
        ("move_right_p2",         "Move Right"),
        ("shoot_p2",              "Shoot"),
        ("switch_weapon_p2",      "Next Weapon"),
        ("switch_weapon_prev_p2", "Prev Weapon"),
        ("knife_p2",              "Knife"),
    };

    // Every rebindable action across both players — used internally wherever P1 and P2 need
    // identical treatment (capturing defaults, applying/loading/resetting bindings, conflict
    // checks). The two public arrays above stay separate because the UI builds two distinct
    // sections from them.
    private static IEnumerable<(string Action, string Label)> AllBindableActions =>
        System.Linq.Enumerable.Concat(BindableActions, BindableActionsP2);

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
        _config.SetValue("audio",    "sfx_volume",   SfxVolume);
        _config.SetValue("audio",    "music_volume", MusicVolume);
        _config.SetValue("audio",    "music_track",  MusicTrackIndex);
        _config.SetValue("player",   "skin",       SkinIndex);
        _config.SetValue("blood",    "enabled",    BloodEnabled);
        _config.SetValue("blood",    "color",      BloodColorIndex);
        foreach (var (action, key) in _bindings)
            _config.SetValue("bindings", action, (int)key);
        _config.Save(SettingsPath);
    }

    private void LoadSettings()
    {
        if (_config.Load(SettingsPath) != Error.Ok) return;
        CameraMode = (CameraMode)(int)_config.GetValue("camera",   "mode",     (int)CameraMode.Shared);
        AimMode    = (AimMode)   (int)_config.GetValue("controls", "aim_mode", (int)AimMode.Movement);
        SfxVolume       = (float) _config.GetValue("audio", "sfx_volume",   0.8f);
        MusicVolume     = (float) _config.GetValue("audio", "music_volume", 0.5f);
        MusicTrackIndex = (int)   _config.GetValue("audio", "music_track",  0);
        SkinIndex  = (int)       _config.GetValue("player", "skin", 0);
        BloodEnabled    = (bool) _config.GetValue("blood", "enabled", true);
        BloodColorIndex = (int)  _config.GetValue("blood", "color", 0);

        foreach (var (action, _) in AllBindableActions)
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
        foreach (var (action, _) in AllBindableActions)
            _config.SetValue("bindings", action, (int)Key.None);
        ApplyAllBindings();
        SaveSettings();
    }

    /// <summary>True if another action already uses this key (so the UI can warn/swap).</summary>
    public string? FindConflict(string action, Key key)
    {
        foreach (var (other, _) in AllBindableActions)
            if (other != action && GetBinding(other) == key) return other;
        return null;
    }

    private void CaptureDefaultBindings()
    {
        foreach (var (action, _) in AllBindableActions)
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
        foreach (var (action, _) in AllBindableActions)
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
