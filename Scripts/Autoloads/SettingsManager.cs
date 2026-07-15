using Godot;

namespace NoBoxHead;

public enum CameraMode { Shared, SplitScreen }

public partial class SettingsManager : Node
{
    public static SettingsManager Instance { get; private set; } = null!;

    public CameraMode CameraMode { get; set; } = CameraMode.Shared;

    private const string SettingsPath = "user://settings.cfg";
    private readonly ConfigFile _config = new();

    public override void _Ready()
    {
        Instance = this;
        LoadSettings();
    }

    public void SaveSettings()
    {
        _config.SetValue("camera", "mode", (int)CameraMode);
        _config.Save(SettingsPath);
    }

    private void LoadSettings()
    {
        if (_config.Load(SettingsPath) == Error.Ok)
            CameraMode = (CameraMode)(int)_config.GetValue("camera", "mode", (int)CameraMode.Shared);
    }
}
