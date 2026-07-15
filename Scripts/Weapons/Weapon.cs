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
    }

    private async void DoReload()
    {
        IsReloading = true;
        EmitSignal(SignalName.Reloading, true);
        await ToSignal(GetTree().CreateTimer(ReloadTime), SceneTreeTimer.SignalName.Timeout);
        int needed  = MagazineSize - CurrentAmmo;
        int take    = Mathf.Min(needed, ReserveAmmo);
        CurrentAmmo += take;
        ReserveAmmo -= take;
        IsReloading  = false;
        EmitSignal(SignalName.Reloading, false);
        EmitSignal(SignalName.AmmoChanged, CurrentAmmo, ReserveAmmo);
    }
}
