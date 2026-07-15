using Godot;
using System;
using System.Collections.Generic;

namespace NoBoxHead;

/// <summary>
/// Lobby screen: host shows PIN and waits; client scans LAN and joins.
/// Pass ?mode=host or ?mode=join in the scene path to set mode.
/// </summary>
public partial class LobbyUI : Control
{
    private bool _isHost;
    private Label? _statusLabel;
    private Label? _pinLabel;
    private VBoxContainer? _gameList;
    private LineEdit? _pinInput;
    private Button? _startBtn;
    private readonly List<string> _connectedPeerLabels = new();
    private int _connectedCount = 1; // host counts as 1

    public override void _Ready()
    {
        // Determine mode from query string (set when changing scene).
        // Godot 4 doesn't natively support query params, so we use a global flag set
        // before calling ChangeSceneToFile.
        _isHost = LobbyMode.IsHost;

        BuildUI();

        if (_isHost)
            StartHost();
        else
            StartJoinScan();
    }

    public override void _ExitTree()
    {
        if (!_isHost)
            NetworkManager.Instance?.StopDiscovery();
    }

    // ── UI Construction ───────────────────────────────────────────────────────

    private void BuildUI()
    {
        var bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.1f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        vbox.CustomMinimumSize = new Vector2(400, 0);
        vbox.Position -= new Vector2(200, 200);
        AddChild(vbox);

        var title = new Label
        {
            Text = _isHost ? "Hosting Game" : "Join Game",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        vbox.AddChild(title);
        vbox.AddChild(Spacer(15));

        if (_isHost)
            BuildHostUI(vbox);
        else
            BuildJoinUI(vbox);

        vbox.AddChild(Spacer(20));
        var back = new Button { Text = "Back", CustomMinimumSize = new Vector2(400, 50) };
        back.Pressed += () =>
        {
            NetworkManager.Instance?.Disconnect();
            GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        };
        vbox.AddChild(back);
    }

    private void BuildHostUI(VBoxContainer root)
    {
        _pinLabel = new Label
        {
            Text = "PIN: ----",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _pinLabel.AddThemeFontSizeOverride("font_size", 50);
        _pinLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.2f));
        root.AddChild(_pinLabel);

        _statusLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, Text = "Waiting for players…" };
        _statusLabel.AddThemeFontSizeOverride("font_size", 16);
        root.AddChild(_statusLabel);

        root.AddChild(Spacer(10));
        _gameList = new VBoxContainer();
        root.AddChild(_gameList);
        root.AddChild(Spacer(10));

        _startBtn = new Button
        {
            Text = "Start Game",
            CustomMinimumSize = new Vector2(400, 55),
            Disabled = false
        };
        _startBtn.AddThemeFontSizeOverride("font_size", 22);
        _startBtn.Pressed += OnStartGamePressed;
        root.AddChild(_startBtn);
    }

    private void BuildJoinUI(VBoxContainer root)
    {
        _statusLabel = new Label { Text = "Scanning for games…", HorizontalAlignment = HorizontalAlignment.Center };
        _statusLabel.AddThemeFontSizeOverride("font_size", 16);
        root.AddChild(_statusLabel);

        root.AddChild(Spacer(10));
        _gameList = new VBoxContainer();
        root.AddChild(_gameList);
        root.AddChild(Spacer(10));

        var hbox = new HBoxContainer();
        root.AddChild(hbox);

        _pinInput = new LineEdit
        {
            PlaceholderText = "Enter PIN",
            CustomMinimumSize = new Vector2(250, 50),
            MaxLength = 4
        };
        _pinInput.AddThemeFontSizeOverride("font_size", 22);
        hbox.AddChild(_pinInput);

        var joinBtn = new Button { Text = "Join", CustomMinimumSize = new Vector2(140, 50) };
        joinBtn.AddThemeFontSizeOverride("font_size", 20);
        joinBtn.Pressed += OnJoinByPinPressed;
        hbox.AddChild(joinBtn);
    }

    // ── Network ───────────────────────────────────────────────────────────────

    private void StartHost()
    {
        NetworkManager.Instance.ServerCreated += pin =>
        {
            if (_pinLabel != null) _pinLabel.Text = $"PIN: {pin}";
        };
        NetworkManager.Instance.PlayerConnected += OnPlayerConnected;

        if (NetworkManager.Instance.CreateServer() != Error.Ok)
        {
            if (_statusLabel != null) _statusLabel.Text = "Failed to create server.";
        }
    }

    private void StartJoinScan()
    {
        NetworkManager.Instance.GamesDiscovered += RefreshGameList;
        NetworkManager.Instance.PlayerIndexAssigned += _ =>
        {
            GetTree().ChangeSceneToFile("res://Scenes/Arena.tscn");
        };
        NetworkManager.Instance.ConnectionFailed += () =>
        {
            if (_statusLabel != null) _statusLabel.Text = "Connection failed. Try again.";
        };
        NetworkManager.Instance.StartDiscovery();
    }

    private void OnPlayerConnected(long peerId)
    {
        _connectedCount++;
        if (_statusLabel != null)
            _statusLabel.Text = $"{_connectedCount} / 4 players connected";

        var label = new Label { Text = $"Player {_connectedCount}" };
        _gameList?.AddChild(label);
    }

    private void RefreshGameList()
    {
        if (_gameList == null) return;
        foreach (var child in _gameList.GetChildren()) child.QueueFree();

        foreach (var game in NetworkManager.Instance.DiscoveredGames)
        {
            var row = new HBoxContainer();
            var lbl = new Label { Text = $"{game.HostName} (PIN: {game.Pin})" };
            lbl.CustomMinimumSize = new Vector2(280, 0);
            var btn = new Button { Text = "Join" };
            btn.Pressed += () =>
            {
                if (_pinInput != null) _pinInput.Text = game.Pin;
            };
            row.AddChild(lbl);
            row.AddChild(btn);
            _gameList.AddChild(row);
        }

        if (_statusLabel != null)
            _statusLabel.Text = NetworkManager.Instance.DiscoveredGames.Count == 0
                ? "Scanning for games…"
                : $"{NetworkManager.Instance.DiscoveredGames.Count} game(s) found";
    }

    private void OnJoinByPinPressed()
    {
        string pin = _pinInput?.Text.Trim() ?? "";
        if (pin.Length != 4) return;

        var err = NetworkManager.Instance.JoinByPin(pin);
        if (err != Error.Ok && _statusLabel != null)
            _statusLabel.Text = $"Could not find host with PIN {pin}.";
    }

    private void OnStartGamePressed()
    {
        NetworkManager.Instance.StopBroadcasting();
        GetTree().ChangeSceneToFile("res://Scenes/Arena.tscn");
    }

    private static Control Spacer(int h) => new Control { CustomMinimumSize = new Vector2(0, h) };
}

/// <summary>Global flag to pass host/join mode between scene transitions.</summary>
public static class LobbyMode
{
    public static bool IsHost { get; set; } = true;
}
