using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoBoxHead;

public struct DiscoveredGame
{
    public string HostName;
    public string HostIp;
    public string Pin;
    public int Port;
}

public partial class NetworkManager : Node
{
    public static NetworkManager Instance { get; private set; } = null!;

    private const int GamePort = 7777;
    private const int DiscoveryPort = 7778;
    private const string DiscoveryMagic = "NOBOXHEAD_V1";

    [Signal] public delegate void PlayerConnectedEventHandler(long peerId);
    [Signal] public delegate void PlayerDisconnectedEventHandler(long peerId);
    [Signal] public delegate void ConnectionFailedEventHandler();
    [Signal] public delegate void ServerCreatedEventHandler(string pin);
    [Signal] public delegate void GamesDiscoveredEventHandler();
    [Signal] public delegate void PlayerIndexAssignedEventHandler(int index);

    public bool IsHost { get; private set; }
    public string CurrentPin { get; private set; } = "";
    public List<DiscoveredGame> DiscoveredGames { get; } = new();

    // Maps peerId → playerIndex (0-3). Host is always index 0 (peerId 1).
    public Dictionary<long, int> PeerPlayerIndex { get; } = new();

    private ENetMultiplayerPeer? _enetPeer;
    private UdpClient? _broadcastSender;
    private UdpClient? _discoveryListener;
    private CancellationTokenSource? _discoveryCts;
    private int _myPlayerIndex;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public override void _ExitTree()
    {
        Disconnect();
    }

    // ── HOST SIDE ──────────────────────────────────────────────────────────────

    public Error CreateServer()
    {
        _enetPeer = new ENetMultiplayerPeer();
        var err = _enetPeer.CreateServer(GamePort, 3); // up to 3 clients + host
        if (err != Error.Ok) return err;

        Multiplayer.MultiplayerPeer = _enetPeer;
        IsHost = true;
        CurrentPin = GeneratePin();
        _myPlayerIndex = 0;
        PeerPlayerIndex[1] = 0; // host peerId is always 1

        StartBroadcasting();
        EmitSignal(SignalName.ServerCreated, CurrentPin);
        GD.Print($"[Network] Server created. PIN: {CurrentPin}");
        return Error.Ok;
    }

    private static string GeneratePin()
    {
        var rng = new Random();
        return rng.Next(1000, 9999).ToString();
    }

    private async void StartBroadcasting()
    {
        _discoveryCts = new CancellationTokenSource();
        var token = _discoveryCts.Token;

        try
        {
            _broadcastSender = new UdpClient { EnableBroadcast = true };
            string localIp = GetLocalIpAddress();
            string hostName = OS.GetEnvironment("USERNAME");
            if (string.IsNullOrEmpty(hostName)) hostName = "Host";

            while (!token.IsCancellationRequested)
            {
                string msg = $"{DiscoveryMagic}|{CurrentPin}|{hostName}|{localIp}|{GamePort}";
                byte[] data = Encoding.UTF8.GetBytes(msg);
                await _broadcastSender.SendAsync(data, data.Length, "255.255.255.255", DiscoveryPort);
                await Task.Delay(1000, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { GD.PrintErr($"[Network] Broadcast error: {e.Message}"); }
    }

    public void StopBroadcasting()
    {
        _discoveryCts?.Cancel();
        _broadcastSender?.Close();
        _broadcastSender = null;
    }

    // ── CLIENT SIDE ───────────────────────────────────────────────────────────

    public void StartDiscovery()
    {
        DiscoveredGames.Clear();
        StopDiscovery();
        _discoveryCts = new CancellationTokenSource();
        _ = ListenForBroadcastsAsync(_discoveryCts.Token);
    }

    public void StopDiscovery()
    {
        _discoveryCts?.Cancel();
        _discoveryListener?.Close();
        _discoveryListener = null;
    }

    private async Task ListenForBroadcastsAsync(CancellationToken token)
    {
        try
        {
            _discoveryListener = new UdpClient(DiscoveryPort) { EnableBroadcast = true };
            while (!token.IsCancellationRequested)
            {
                var result = await _discoveryListener.ReceiveAsync(token);
                string msg = Encoding.UTF8.GetString(result.Buffer);
                CallDeferred(MethodName.ParseDiscoveryMessage, msg);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { GD.PrintErr($"[Network] Discovery error: {e.Message}"); }
    }

    // Called on the main thread via CallDeferred.
    private void ParseDiscoveryMessage(string message)
    {
        var parts = message.Split('|');
        if (parts.Length < 5 || parts[0] != DiscoveryMagic) return;

        string pin = parts[1];
        if (DiscoveredGames.Any(g => g.Pin == pin)) return;

        DiscoveredGames.Add(new DiscoveredGame
        {
            Pin = pin,
            HostName = parts[2],
            HostIp = parts[3],
            Port = int.TryParse(parts[4], out int port) ? port : GamePort
        });

        EmitSignal(SignalName.GamesDiscovered);
    }

    public Error JoinByPin(string pin)
    {
        var game = DiscoveredGames.FirstOrDefault(g => g.Pin == pin);
        if (game.HostIp == null) return Error.Failed;
        return JoinServer(game.HostIp, game.Port);
    }

    public Error JoinServer(string ip, int port)
    {
        StopDiscovery();
        _enetPeer = new ENetMultiplayerPeer();
        var err = _enetPeer.CreateClient(ip, port);
        if (err != Error.Ok) return err;

        Multiplayer.MultiplayerPeer = _enetPeer;
        IsHost = false;
        GD.Print($"[Network] Joining {ip}:{port}");
        return Error.Ok;
    }

    // ── MULTIPLAYER CALLBACKS ─────────────────────────────────────────────────

    private void OnPeerConnected(long id)
    {
        GD.Print($"[Network] Peer connected: {id}");
        if (IsHost)
        {
            int idx = PeerPlayerIndex.Count; // next available index
            PeerPlayerIndex[id] = idx;
            RpcId(id, MethodName.ReceivePlayerIndex, idx);
        }
        EmitSignal(SignalName.PlayerConnected, id);
    }

    private void OnPeerDisconnected(long id)
    {
        GD.Print($"[Network] Peer disconnected: {id}");
        PeerPlayerIndex.Remove(id);
        EmitSignal(SignalName.PlayerDisconnected, id);
    }

    private void OnConnectedToServer() => GD.Print("[Network] Connected to server.");

    private void OnConnectionFailed()
    {
        GD.Print("[Network] Connection failed.");
        EmitSignal(SignalName.ConnectionFailed);
    }

    private void OnServerDisconnected()
    {
        GD.Print("[Network] Server disconnected.");
        Multiplayer.MultiplayerPeer = null;
    }

    // ── RPCs ──────────────────────────────────────────────────────────────────

    // Host → client: tell the client which player slot they occupy.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    private void ReceivePlayerIndex(int index)
    {
        _myPlayerIndex = index;
        PeerPlayerIndex[Multiplayer.GetUniqueId()] = index;
        GD.Print($"[Network] My player index: {index}");
        EmitSignal(SignalName.PlayerIndexAssigned, index);
    }

    // ── PUBLIC HELPERS ────────────────────────────────────────────────────────

    public int GetMyPlayerIndex() => _myPlayerIndex;

    public int GetConnectedPlayerCount() => PeerPlayerIndex.Count;

    public void Disconnect()
    {
        StopBroadcasting();
        StopDiscovery();
        if (Multiplayer.HasMultiplayerPeer())
            Multiplayer.MultiplayerPeer = null;
        _enetPeer = null;
        IsHost = false;
        CurrentPin = "";
        PeerPlayerIndex.Clear();
        DiscoveredGames.Clear();
    }

    public static string GetLocalIpAddress()
    {
        try
        {
            using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            sock.Connect("8.8.8.8", 65530);
            return ((IPEndPoint)sock.LocalEndPoint!).Address.ToString();
        }
        catch { return "127.0.0.1"; }
    }
}
