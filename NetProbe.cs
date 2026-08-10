using Godot;
using NoBoxHead;
using System.Linq;

// TEMPORARY: demon fireballs. Host spawns a demon next to each player; we watch how many
// projectiles exist on each peer and whether either player actually loses health.
public partial class NetProbe : Node
{
    private bool _isHost;
    private bool _arenaLoaded;
    private bool _demonsSpawned;
    private int  _frame;
    private int  _peakProjectiles;

    public override void _Ready()
    {
        _isHost = OS.GetCmdlineUserArgs().Contains("--host");
        GD.Print($"[PROBE] role = {(_isHost ? "HOST" : "CLIENT")}");

        NetworkManager.Instance.GameStarting += LoadArena;

        if (_isHost)
        {
            GD.Print($"[PROBE] CreateServer -> {NetworkManager.Instance.CreateServer()}");
            NetworkManager.Instance.PlayerConnected += _ =>
                GetTree().CreateTimer(1.0).Timeout += () =>
                {
                    if (!_arenaLoaded) NetworkManager.Instance.BroadcastGameStarting();
                };
        }
        else
        {
            GD.Print($"[PROBE] JoinServer -> {NetworkManager.Instance.JoinServer("127.0.0.1", 7777)}");
        }
    }

    private void LoadArena()
    {
        if (_arenaLoaded) return;
        _arenaLoaded = true;
        AddChild(ResourceLoader.Load<PackedScene>("res://Scenes/Arena.tscn").Instantiate());
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (!_arenaLoaded) return;

        // Host drops a demon beside each player so both are inside ShootRange (250).
        if (_isHost && !_demonsSpawned && _frame > 420)
        {
            var players = GetTree().GetNodesInGroup("players").Cast<Player>().ToList();
            if (players.Count >= 2)
            {
                _demonsSpawned = true;
                var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Demon.tscn");
                var bullets = FindByName(this, "Bullets");
                foreach (var p in players)
                {
                    var d = scene.Instantiate<Demon>();
                    d.Name = $"TestDemon{p.PlayerIndex}";
                    FindByName(this, "Entities")?.AddChild(d);
                    d.GlobalPosition = p.GlobalPosition + new Vector2(120, 0);
                    if (bullets != null) d.SetProjectileContainer(bullets);
                    GD.Print($"[PROBE] spawned demon beside {p.Name} at {d.GlobalPosition}");
                }
            }
        }

        int projectiles = Count<DemonProjectile>(this);
        if (projectiles > _peakProjectiles) _peakProjectiles = projectiles;

        if (_frame % 180 != 0) return;
        if (_frame > 1500) { GetTree().Quit(); return; }

        string hp = string.Join("  ", GetTree().GetNodesInGroup("players").Cast<Player>()
            .OrderBy(p => p.Name.ToString())
            .Select(p => $"{p.Name}={p.CurrentHealth:F0}"));
        GD.Print($"[PROBE] t={_frame / 60}s projectiles={projectiles} peak={_peakProjectiles}  hp: {hp}");
    }

    private static int Count<T>(Node n) where T : Node
    {
        int c = n is T ? 1 : 0;
        foreach (var ch in n.GetChildren()) c += Count<T>(ch);
        return c;
    }

    private static Node? FindByName(Node n, string name)
    {
        if (n.Name == name) return n;
        foreach (var c in n.GetChildren())
        {
            var r = FindByName(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
