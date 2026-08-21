using Godot;

namespace NoBoxHead;

/// <summary>
/// Deployable auto-turret. Every peer instantiates one at the same spot (mirrored by
/// TurretWeapon.PlaceTurretRpc, same placement-mirroring pattern as ProximityMine/
/// RocketProjectile), but — following EnemyBase's host-authoritative pattern — only the HOST's
/// copy scans for a target and calls TakeDamage; every other peer's copy short-circuits out of
/// targeting entirely and only plays the muzzle tracer the host RPCs it (ShowShotRpc), the same
/// way Demon mirrors its fireball. No Cosmetic flag is needed here (unlike Mine/Rocket): damage
/// is gated by _isHost, not by who placed it, so there is never a peer that both fires and
/// resolves damage independently — exactly one copy (the host's) ever does.
/// </summary>
public partial class Turret : Node2D
{
    [Export] public float Damage         = 14f;
    [Export] public float FireRate       = 0.5f;
    [Export] public float Range          = 260f;
    [Export] public float Lifetime       = 14f;
    [Export] public float KnockbackForce = 40f;

    private bool  _isHost;
    private float _fireTimer;
    private float _lifeTimer;

    public override void _Ready()
    {
        _isHost    = !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
        _lifeTimer = Lifetime;

        AddChild(new ColorRect
        {
            Color    = new Color(0.28f, 0.3f, 0.34f),
            Size     = new Vector2(26, 26),
            Position = new Vector2(-13, -13),
        });
        AddChild(new ColorRect
        {
            Color    = new Color(0.55f, 0.55f, 0.62f),
            Size     = new Vector2(10, 10),
            Position = new Vector2(-5, -5),
            ZIndex   = 1,
        });
    }

    public override void _PhysicsProcess(double delta)
    {
        // Each peer counts its own copy's lifetime down independently. That's only cosmetic
        // timing jitter (a frame or two of drift at most) since the copies that matter for
        // gameplay — the host's targeting/damage — are already gated by _isHost below; there is
        // no shared state here worth spending an RPC keeping in lockstep.
        _lifeTimer -= (float)delta;
        if (_lifeTimer <= 0f) { QueueFree(); return; }

        if (!_isHost) return;

        _fireTimer -= (float)delta;
        if (_fireTimer > 0f) return;

        var target = FindNearestEnemy();
        if (target == null) return;

        Fire(target);
        _fireTimer = FireRate;
    }

    private Node2D? FindNearestEnemy()
    {
        Node2D? nearest = null;
        float   minDist = Range;
        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is IDamageable d && d.IsAlive && node is Node2D n2d)
            {
                float dist = GlobalPosition.DistanceTo(n2d.GlobalPosition);
                if (dist < minDist) { minDist = dist; nearest = n2d; }
            }
        }
        return nearest;
    }

    private void Fire(Node2D target)
    {
        var toTarget = target.GlobalPosition - GlobalPosition;
        var dir      = toTarget.LengthSquared() > 1f ? toTarget.Normalized() : Vector2.Down;

        if (target is IDamageable d) d.TakeDamage(Damage);
        if (target is IKnockbackable kb) kb.ApplyKnockback(dir * KnockbackForce);
        BloodSystem.Instance?.Splatter(target.GlobalPosition, dir, 0.3f);

        if (NetworkManager.IsNetworked) Rpc(MethodName.ShowShotRpc, target.GlobalPosition);
        else ShowShotRpc(target.GlobalPosition);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
         TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ShowShotRpc(Vector2 targetPos)
    {
        var line = new Line2D
        {
            Points       = new[] { Vector2.Zero, ToLocal(targetPos) },
            Width        = 2f,
            DefaultColor = new Color(1f, 0.85f, 0.2f, 0.9f),
            ZIndex       = 4,
        };
        AddChild(line);

        var tween = line.CreateTween();
        tween.TweenProperty(line, "modulate:a", 0f, 0.12f);
        tween.TweenCallback(Godot.Callable.From(line.QueueFree));
    }
}
