using Godot;

namespace NoBoxHead;

/// <summary>
/// Abstract base for all weapons.
/// Concrete weapons set stats and override ShootBullet().
/// </summary>
public abstract partial class Weapon : Node
{
    [Signal] public delegate void AmmoChangedEventHandler(int current, int max);
    [Signal] public delegate void ReloadingEventHandler(bool isReloading);

    [Export] public float FireRate = 0.4f;   // seconds between shots
    [Export] public int MagazineSize = 12;
    [Export] public float ReloadTime = 1.5f;
    [Export] public float BulletDamage = 15f;

    public int CurrentAmmo { get; protected set; }
    public bool IsReloading { get; private set; }

    protected float _fireCooldown;
    protected PackedScene? BulletScene;

    public override void _Ready()
    {
        CurrentAmmo = MagazineSize;
        BulletScene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/Bullet.tscn");
    }

    public override void _Process(double delta)
    {
        if (_fireCooldown > 0f)
            _fireCooldown -= (float)delta;
    }

    public void TryShoot(Vector2 origin, Vector2 direction)
    {
        if (IsReloading || _fireCooldown > 0f) return;

        if (CurrentAmmo <= 0)
        {
            DoReload();
            return;
        }

        SpawnBullet(origin, direction);
        CurrentAmmo--;
        _fireCooldown = FireRate;
        EmitSignal(SignalName.AmmoChanged, CurrentAmmo, MagazineSize);

        if (CurrentAmmo == 0)
            DoReload();
    }

    public void RequestReload()
    {
        if (!IsReloading && CurrentAmmo < MagazineSize)
            DoReload();
    }

    protected virtual void SpawnBullet(Vector2 origin, Vector2 direction)
    {
        if (BulletScene == null) return;
        var bullet = BulletScene.Instantiate<Bullet>();
        bullet.Damage = BulletDamage;
        GetTree().Root.AddChild(bullet);
        bullet.Init(origin, direction, BulletDamage);
    }

    private async void DoReload()
    {
        IsReloading = true;
        EmitSignal(SignalName.Reloading, true);
        await ToSignal(GetTree().CreateTimer(ReloadTime), SceneTreeTimer.SignalName.Timeout);
        CurrentAmmo = MagazineSize;
        IsReloading = false;
        EmitSignal(SignalName.Reloading, false);
        EmitSignal(SignalName.AmmoChanged, CurrentAmmo, MagazineSize);
    }
}
