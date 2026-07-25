using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

/// <summary>
/// Plays one-shot sound effects from a fixed pool of players, so overlapping sounds (a
/// machine gun burst, a crowd of zombies dying) never allocate nodes mid-game and can't
/// exceed a set number of simultaneous voices.
///
/// Sounds are looked up by the filenames in Assets/Audio/SFX — see Tools/generate_sfx.py.
/// Dropping better .wav files in with the same names replaces them with no code changes.
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; } = null!;

    // Sfx names, kept as constants so call sites don't hardcode strings.
    public const string Pistol       = "pistol";
    public const string Shotgun      = "shotgun";
    public const string MachineGun   = "machinegun";
    public const string Knife        = "knife";
    public const string Explosion    = "explosion";
    public const string Hit          = "hit";
    public const string EnemyDeath   = "enemy_death";
    public const string PlayerHurt   = "player_hurt";
    public const string PickupAmmo   = "pickup_ammo";
    public const string PickupHealth = "pickup_health";
    public const string BarrelPlace  = "barrel_place";
    public const string WaveStart    = "wave_start";
    public const string GameOver     = "game_over";

    private const int VoiceCount = 16;
    private const string SfxPath = "res://Assets/Audio/SFX/";

    private readonly Dictionary<string, AudioStream> _streams = new();
    private readonly List<AudioStreamPlayer>         _voices  = new();
    private int _nextVoice;

    public override void _Ready()
    {
        Instance = this;
        // Keep audio alive through the pause menu (button clicks, and so a sound that was
        // already playing doesn't get cut off mid-tail when the tree pauses).
        ProcessMode = ProcessModeEnum.Always;

        LoadStreams();

        for (int i = 0; i < VoiceCount; i++)
        {
            var player = new AudioStreamPlayer { Bus = "Master", ProcessMode = ProcessModeEnum.Always };
            AddChild(player);
            _voices.Add(player);
        }

        ApplyVolume();
    }

    private void LoadStreams()
    {
        string[] names =
        {
            Pistol, Shotgun, MachineGun, Knife, Explosion, Hit, EnemyDeath,
            PlayerHurt, PickupAmmo, PickupHealth, BarrelPlace, WaveStart, GameOver,
        };

        foreach (var name in names)
        {
            var stream = ResourceLoader.Load<AudioStream>($"{SfxPath}{name}.wav");
            if (stream != null) _streams[name] = stream;
            else GD.PushWarning($"AudioManager: missing sound '{name}.wav'");
        }
    }

    /// <summary>Plays a sound effect. Unknown names are ignored rather than throwing.</summary>
    public void Play(string name, float volumeScale = 1f, float pitchVariance = 0f)
    {
        if (Muted || !_streams.TryGetValue(name, out var stream)) return;

        var voice = NextFreeVoice();
        voice.Stream      = stream;
        voice.VolumeDb    = Mathf.LinearToDb(Mathf.Clamp(Volume * volumeScale, 0.0001f, 1f));
        // Slight random pitch keeps repeated sounds (gunfire, hits) from sounding robotic.
        voice.PitchScale  = pitchVariance > 0f
            ? 1f + (GD.Randf() * 2f - 1f) * pitchVariance
            : 1f;
        voice.Play();
    }

    // Round-robin, preferring an idle voice so we don't cut off a sound that's still playing.
    private AudioStreamPlayer NextFreeVoice()
    {
        foreach (var voice in _voices)
            if (!voice.Playing) return voice;

        var fallback = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _voices.Count;
        return fallback;
    }

    // ── Volume ────────────────────────────────────────────────────────────────

    public float Volume => SettingsManager.Instance?.SfxVolume ?? 0.8f;
    public bool  Muted  => Volume <= 0.001f;

    /// <summary>Called by the settings UI after changing the volume.</summary>
    public void ApplyVolume()
    {
        foreach (var voice in _voices)
            voice.VolumeDb = Mathf.LinearToDb(Mathf.Clamp(Volume, 0.0001f, 1f));
    }
}
