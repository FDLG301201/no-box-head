using Godot;

namespace NoBoxHead;

public enum CameraMode { Shared, SplitScreen }
public enum AimMode    { Movement, Mouse, AutoAim }
public enum GameMode   { SinglePlayer, LocalCoop }

public partial class SettingsManager : Node
{
    public static SettingsManager Instance { get; private set; } = null!;

    public CameraMode CameraMode { get; set; } = CameraMode.Shared;
    public AimMode    AimMode    { get; set; } = AimMode.Movement;
    // Session-only, not persisted to disk.
    public GameMode   GameMode   { get; set; } = GameMode.SinglePlayer;
    public ArenaType  ArenaType  { get; set; } = ArenaType.Classic;
    // Seed for the Random arena; assigned when a game starts so the whole session matches.
    public ulong      ArenaSeed  { get; set; }

    private const string SettingsPath = "user://settings.cfg";
    private readonly ConfigFile _config = new();

    public override void _Ready()
    {
        Instance = this;
        LoadSettings();
    }

    public void SaveSettings()
    {
        _config.SetValue("camera",   "mode",     (int)CameraMode);
        _config.SetValue("controls", "aim_mode", (int)AimMode);
        _config.Save(SettingsPath);
    }

    private void LoadSettings()
    {
        if (_config.Load(SettingsPath) != Error.Ok) return;
        CameraMode = (CameraMode)(int)_config.GetValue("camera",   "mode",     (int)CameraMode.Shared);
        AimMode    = (AimMode)   (int)_config.GetValue("controls", "aim_mode", (int)AimMode.Movement);
    }
}
