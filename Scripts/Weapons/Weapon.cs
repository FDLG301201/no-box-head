using Godot;

namespace NoBoxHead;

/// <summary>
/// Abstract base for all weapons.
/// Each weapon defines its own magazine size, reserve, and reload time.
/// Auto-reloads from ReserveAmmo when the magazine empties. No reload button.
/// </summary>
public abstract partial class Weapon : Node
{
    [Signal] public delegate void AmmoChangedEventHandler(int current, int reserve);
    [Signal] public delegate void ReloadingEventHandler(bool isReloading);

    [Export] public float FireRate         = 0.4f;
    [Export] public int   MagazineSize     = 12;
    [Export] public float ReloadTime       = 1.5f;
    [Export] public float BulletDamage     = 15f;
    [Export] public int   StartReserveAmmo = 12;
    [Export] public int   MaxReserveAmmo   = 60;
    [Export] public float BulletKnockback  = 100f;

    // Human-readable name shown in HUD.
    public virtual string WeaponName => GetType().Name;

    // Sound played on a successful shot; subclasses override with their own.
    protected virtual string FireSound => AudioManager.Pistol;

    public int  CurrentAmmo { get; protected set; }
    public int  ReserveAmmo { get; private set; }
    public bool IsReloading { get; private set; }

    // Route bullets to a specific scene-tree container instead of root.
    public Node? BulletContainer { get; set; }

    protected float        _fireCooldown;
    protected PackedScene? BulletScene;

    public override void _Ready()
    {
        CurrentAmmo = MagazineSize;
        ReserveAmmo = StartReserveAmmo;
        BulletScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Bullet.tscn");
        EmitSignal(SignalName.AmmoChanged, CurrentAmmo, ReserveAmmo);
    }

    public override void _Process(double delta)
    {
        if (_fireCooldown > 0f) _fireCooldown -= (float)delta;
    }

    public virtual void TryShoot(Vector2 origin, Vector2 direction)
    {
        if (IsReloading || _fireCooldown > 0f) return;

        if (CurrentAmmo <= 0)
        {
            if (ReserveAmmo > 0) DoReload();
            return;
        }

        SpawnBullet(origin, direction);
        AudioManager.Instance?.Play(FireSound, 1f, 0.08f);
        CurrentAmmo--;
        _fireCooldown = FireRate;
        EmitSignal(SignalName.AmmoChanged, CurrentAmmo, ReserveAmmo);

        if (CurrentAmmo == 0 && ReserveAmmo > 0)
            DoReload();
    }

    // Called by AmmoPack pickup via Player.AddAmmo().
    public void AddReserveAmmo(int amount)
    {
        ReserveAmmo = Mathf.Min(ReserveAmmo + amount, MaxReserveAmmo);
        EmitSignal(SignalName.AmmoChanged, CurrentAmmo, ReserveAmmo);
    }

    protected virtual void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        if (BulletScene == null) return;
        var bullet = BulletScene.Instantiate<Bullet>();
        bullet.Damage         = BulletDamage;
        bullet.KnockbackForce = BulletKnockback;
        (BulletContainer ?? GetTree().Root).AddChild(bullet);
        bullet.Init(origin, direction, BulletDamage);

        BroadcastTracer(origin, direction, bullet.MaxDistance, bullet.Speed);
    }

    /// <summary>
    /// Mirrors a shot to the other players so they can see it being fired. Weapons are named
    /// Weapon0/Weapon1 under a Player0/Player1, so this node's path is identical on every peer
    /// and the RPC routes cleanly. Unreliable: a dropped tracer costs one missing muzzle
    /// streak, which is not worth re-sending.
    /// </summary>
    protected void BroadcastTracer(Vector2 origin, Vector2 direction, float maxDistance, float speed)
    {
        if (NetworkManager.IsNetworked)
            Rpc(MethodName.SpawnTracerRpc, origin, direction, maxDistance, speed);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SpawnTracerRpc(Vector2 origin, Vector2 direction, float maxDistance, float speed)
    {
        if (BulletScene == null) return;
        var tracer = BulletScene.Instantiate<Bullet>();
        tracer.Cosmetic    = true; // visual only — the shooter's round does the damage
        tracer.MaxDistance = maxDistance;
        tracer.Speed       = speed;
        (BulletContainer ?? GetTree().Root).AddChild(tracer);
        tracer.Init(origin, direction, 0f);
    }

    private async void DoReload()
    {
        IsReloading = true;
        EmitSignal(SignalName.Reloading, true);
        // processAlways:false so reloads don't tick down while the game is paused.
        await ToSignal(GetTree().CreateTimer(ReloadTime, false), SceneTreeTimer.SignalName.Timeout);
        int needed  = MagazineSize - CurrentAmmo;
        int take    = Mathf.Min(needed, ReserveAmmo);
        CurrentAmmo += take;
        ReserveAmmo -= take;
        IsReloading  = false;
        EmitSignal(SignalName.Reloading, false);
        EmitSignal(SignalName.AmmoChanged, CurrentAmmo, ReserveAmmo);
    }
}
