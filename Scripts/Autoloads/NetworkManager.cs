using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
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

    public const int GamePort = 7777;
    private const int DiscoveryPort = 7778;
    private const string DiscoveryMagic = "NOBOXHEAD_V1";

    [Signal] public delegate void PlayerConnectedEventHandler(long peerId);
    [Signal] public delegate void PlayerDisconnectedEventHandler(long peerId);
    [Signal] public delegate void ConnectionFailedEventHandler();
    [Signal] public delegate void ServerCreatedEventHandler(string pin);
    [Signal] public delegate void GamesDiscoveredEventHandler();
    [Signal] public delegate void PlayerIndexAssignedEventHandler(int index);
    // Fired on every peer (host included) at the exact moment the host presses "Start Game",
    // so nobody races ahead into Arena.tscn on their own — see BroadcastGameStarting.
    [Signal] public delegate void GameStartingEventHandler();

    /// <summary>
    /// True only when a real network session is active. Note that Godot's
    /// Multiplayer.HasMultiplayerPeer() is NOT a reliable offline check: the API defaults to
    /// an OfflineMultiplayerPeer, so it reports true until something nulls the peer (which
    /// only happens once MainMenu calls Disconnect()).
    /// </summary>
    public static bool IsNetworked =>
        Instance != null &&
        Instance.Multiplayer.MultiplayerPeer is not null and not OfflineMultiplayerPeer;

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
            string hostName = OS.GetEnvironment("USERNAME");
            if (string.IsNullOrEmpty(hostName)) hostName = "Host";

            while (!token.IsCancellationRequested)
            {
                // One packet per local network, each carrying that network's own address.
                // A phone hosting while sharing its mobile data has TWO networks: the cellular
                // one (which is the default route, so a single 255.255.255.255 send and
                // GetLocalIpAddress both pick it) and the hotspot LAN where the other player
                // actually is. Advertising only the default route meant the phone announced an
                // unreachable carrier IP onto a network nobody was listening on — which is why
                // a PC-hosted game was visible to the phone but never the other way round.
                foreach (var (broadcast, local) in GetBroadcastTargets())
                {
                    string msg = $"{DiscoveryMagic}|{CurrentPin}|{hostName}|{local}|{GamePort}";
                    byte[] data = Encoding.UTF8.GetBytes(msg);
                    try { await _broadcastSender.SendAsync(data, data.Length, broadcast.ToString(), DiscoveryPort); }
                    catch (SocketException) { /* interface went away mid-loop; try the rest */ }
                }

                // Kept as a fallback for any platform where interface enumeration comes back
                // empty (some Android builds restrict it), so discovery never gets worse.
                string fallback = $"{DiscoveryMagic}|{CurrentPin}|{hostName}|{GetLocalIpAddress()}|{GamePort}";
                byte[] fallbackData = Encoding.UTF8.GetBytes(fallback);
                try { await _broadcastSender.SendAsync(fallbackData, fallbackData.Length, "255.255.255.255", DiscoveryPort); }
                catch (SocketException) { }

                await Task.Delay(1000, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { GD.PrintErr($"[Network] Broadcast error: {e.Message}"); }
    }

    /// <summary>
    /// Every up, non-loopback IPv4 network this machine is on, as (directed broadcast, own
    /// address). Sending one packet per entry reaches a hotspot LAN even when the default
    /// route points somewhere else entirely, like a phone's mobile data.
    /// </summary>
    public static List<(IPAddress Broadcast, IPAddress Local)> GetBroadcastTargets()
    {
        var targets = new List<(IPAddress, IPAddress)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var mask = addr.IPv4Mask;
                    if (mask == null) continue;

                    var ip        = addr.Address.GetAddressBytes();
                    var maskBytes = mask.GetAddressBytes();
                    var broadcast = new byte[4];
                    for (int i = 0; i < 4; i++)
                        broadcast[i] = (byte)(ip[i] | ~maskBytes[i]);

                    targets.Add((new IPAddress(broadcast), addr.Address));
                }
            }
        }
        catch (Exception e) { GD.PrintErr($"[Network] Interface scan failed: {e.Message}"); }
        return targets;
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
                // The sender's own view of its address can be wrong for the network we heard it
                // on (a phone on mobile data reports its carrier IP). Where the packet actually
                // came from is reachable by definition, so that wins.
                CallDeferred(MethodName.ParseDiscoveryMessage, msg,
                             result.RemoteEndPoint.Address.ToString());
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { GD.PrintErr($"[Network] Discovery error: {e.Message}"); }
    }

    // Called on the main thread via CallDeferred.
    private void ParseDiscoveryMessage(string message, string sourceIp)
    {
        var parts = message.Split('|');
        if (parts.Length < 5 || parts[0] != DiscoveryMagic) return;

        string pin = parts[1];
        if (DiscoveredGames.Any(g => g.Pin == pin)) return;

        DiscoveredGames.Add(new DiscoveredGame
        {
            Pin = pin,
            HostName = parts[2],
            // parts[3] is the host's self-reported address, kept only as a fallback: it is the
            // one thing in the packet that can name an interface we cannot reach.
            HostIp = !string.IsNullOrEmpty(sourceIp) ? sourceIp : parts[3],
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

    // Host → everyone (including itself, CallLocal = true): the game is starting now. Every
    // peer's LobbyUI listens for this and changes to Arena.tscn at the same moment, instead of
    // a client jumping there the instant they connect — which used to drop them into an empty
    // arena (no local player spawned yet, no enemies) until the host got around to starting.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void NotifyGameStarting() => EmitSignal(SignalName.GameStarting);

    /// <summary>Host-only: tells every connected peer to transition to the arena now.</summary>
    public void BroadcastGameStarting() => Rpc(MethodName.NotifyGameStarting);

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
