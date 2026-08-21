using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

/// <summary>
/// Background music: looping playback, its own volume, and crossfading between tracks.
///
/// Deliberately separate from AudioManager rather than bolted onto it. AudioManager is a
/// 16-voice round-robin pool for one-shot effects on the Master bus; music is one long looping
/// stream that needs its own volume control and has to survive scene changes. Sharing the pool
/// would let a busy firefight steal the music's voice.
///
/// The "Music" bus is created in code instead of shipping an AudioBusLayout resource, matching
/// how the rest of this project builds things procedurally — and it means the bus cannot go
/// missing if the resource is not imported on a fresh clone.
/// </summary>
public partial class MusicManager : Node
{
    public static MusicManager Instance { get; private set; } = null!;

    public const string BattleChiptune = "chiptune_battle";

    /// <summary>
    /// Registry of selectable tracks: display name → resource path. Drop a new file into
    /// Assets/Audio/Music/, add a line here, and it shows up in the settings picker.
    /// </summary>
    public static readonly (string Id, string Label, string Path)[] Tracks =
    {
        (BattleChiptune, "8-Bit Battle", "res://Assets/Audio/Music/chiptune_battle.mp3"),
    };

    private const string MusicBus = "Music";

    private AudioStreamPlayer _playerA = null!;
    private AudioStreamPlayer _playerB = null!;
    private AudioStreamPlayer _active  = null!;   // the one currently audible
    private Tween?            _fade;
    private string?           _currentId;

    private readonly Dictionary<string, AudioStream> _streams = new();

    public override void _Ready()
    {
        Instance = this;
        // Music must keep playing while the tree is paused, or pausing would kill the track.
        ProcessMode = ProcessModeEnum.Always;

        EnsureMusicBus();
        _playerA = MakePlayer("MusicA");
        _playerB = MakePlayer("MusicB");
        _active  = _playerA;

        foreach (var (id, _, path) in Tracks)
        {
            var stream = ResourceLoader.Load<AudioStream>(path);
            if (stream != null) _streams[id] = stream;
            else GD.PushWarning($"[Music] missing track asset: {path}");
        }

        ApplyVolume();
    }

    private AudioStreamPlayer MakePlayer(string name)
    {
        var p = new AudioStreamPlayer { Name = name, Bus = MusicBus, VolumeDb = LinearToDb(0f) };
        p.ProcessMode = ProcessModeEnum.Always;
        AddChild(p);
        return p;
    }

    private static void EnsureMusicBus()
    {
        if (AudioServer.GetBusIndex(MusicBus) != -1) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, MusicBus);
        AudioServer.SetBusSend(idx, "Master");
    }

    // Godot mutes at -80 dB; going lower just wastes range, and 0 linear must be truly silent.
    private static float LinearToDb(float linear) =>
        linear <= 0.001f ? -80f : Mathf.LinearToDb(linear);

    /// <summary>Re-applies the persisted music volume to the bus. Called by the settings UI.</summary>
    public void ApplyVolume()
    {
        int bus = AudioServer.GetBusIndex(MusicBus);
        if (bus == -1) return;
        float v = SettingsManager.Instance?.MusicVolume ?? 0.5f;
        AudioServer.SetBusVolumeDb(bus, LinearToDb(v));
        AudioServer.SetBusMute(bus, v <= 0.001f);
    }

    /// <summary>
    /// Starts <paramref name="id"/>, crossfading from whatever is playing. Re-requesting the
    /// track that is already playing does nothing, so callers can fire this on every scene load
    /// without restarting the song each time.
    /// </summary>
    public void Play(string id, float fadeSeconds = 1.2f)
    {
        if (_currentId == id && _active.Playing) return;
        if (!_streams.TryGetValue(id, out var stream)) return;

        var incoming = _active == _playerA ? _playerB : _playerA;
        var outgoing = _active;

        incoming.Stream   = stream;
        incoming.VolumeDb = LinearToDb(0f);
        incoming.Play();

        _fade?.Kill();
        _fade = CreateTween();
        _fade.SetParallel(true);
        // Both players sit on the Music bus, so these are relative fades under the master
        // music volume — the user's volume setting still governs the final level.
        _fade.TweenProperty(incoming, "volume_db", LinearToDb(1f), fadeSeconds);
        if (outgoing.Playing)
        {
            _fade.TweenProperty(outgoing, "volume_db", LinearToDb(0f), fadeSeconds);
            _fade.Chain().TweenCallback(Callable.From(outgoing.Stop));
        }

        _active    = incoming;
        _currentId = id;
    }

    /// <summary>Plays whichever track the player picked in Settings.</summary>
    public void PlaySelected(float fadeSeconds = 1.2f)
    {
        int i = SettingsManager.Instance?.MusicTrackIndex ?? 0;
        Play(Tracks[Mathf.Clamp(i, 0, Tracks.Length - 1)].Id, fadeSeconds);
    }

    public void Stop(float fadeSeconds = 0.8f)
    {
        _fade?.Kill();
        _fade = CreateTween();
        _fade.TweenProperty(_active, "volume_db", LinearToDb(0f), fadeSeconds);
        _fade.TweenCallback(Callable.From(_active.Stop));
        _currentId = null;
    }
}
