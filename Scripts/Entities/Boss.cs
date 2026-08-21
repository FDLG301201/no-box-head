using Godot;

namespace NoBoxHead;

/// <summary>
/// Wave boss. Unlike the Ogre — which is only a stat-scaled zombie — this one has phases: it
/// changes how it fights as it loses health, and announces itself so the HUD can show a bar and
/// the music can react.
///
/// Phase changes are replicated explicitly. Everything else about an enemy reaches clients
/// through EnemyBase's position/health sync, but a phase is derived state: a client computing it
/// from its own copy of the health would flip at a slightly different moment, and the boss would
/// visibly change colour and speed at different times on each screen.
///
/// THE BUTCHER's signature threat is a shell that bursts into a star of bullets (see
/// <see cref="UpdateSignatureAttack"/> / <see cref="FireShell"/> / BossShell / BossBurstBullet).
/// BossGigante subclasses this instead of EnemyBase directly so it stays a `Boss` for HUD
/// purposes (HUD.OnBossSpawned/_Process key off `is Boss` for the name and health bar) while
/// swapping in its own attack via the same UpdateSignatureAttack hook.
/// </summary>
public partial class Boss : EnemyBase
{
    /// <summary>Fraction of max health at which the boss enters its second phase.</summary>
    private const float EnragedAtHealthFraction = 0.5f;

    // Phase 2 multipliers. It gets faster and hits harder, but its reach does not change —
    // a boss that also gained range would be unreadable to fight.
    private const float EnragedSpeedMultiplier  = 1.55f;
    private const float EnragedDamageMultiplier = 1.4f;

    // ── Signature attack: a slow shell that bursts into a star of 8 bullets ────────────────
    // Slow and visibly telegraphed (the shell swells as its fuse burns — see BossShell) so the
    // burst itself, not the shell's flight, is the actual threat: a player who reads the swell
    // has the whole fuse to walk out of where the star is about to form. 90 px/s flight, 1.6s
    // fuse — slower than Demon's fireball (200 px/s) on purpose, since this is a zone-denial
    // read rather than a snap-reaction dodge. Burst: 8 bullets at 260 px/s for 16 dmg each.
    //
    // Cadence is tied to the existing two phases rather than a continuous ramp off health: the
    // decision of *when* to fire only ever runs on the host (UpdateSignatureAttack is only
    // reached from the _isHost branch in _PhysicsProcess below) and each shot is mirrored
    // individually via SpawnShellRpc, so there is nothing a client needs to compute from health
    // in the first place. Reusing _enraged — already replicated via EnterEnragedRpc — is free;
    // a continuous health-driven cadence would mean reading a value clients only get over an
    // UNRELIABLE sync (EnemyBase.SyncEnemyState) for no actual benefit.
    private const float ShellCooldownNormal  = 3.2f;
    private const float ShellCooldownEnraged = 1.6f; // exactly 2x cadence once enraged
    private const float ShellRange           = 340f;

    public virtual string BossName => "THE BUTCHER";

    private bool  _enraged;
    private float _baseSpeed;
    private float _baseDamage;

    private float        _shellTimer;
    private PackedScene? _shellScene;
    private Node?        _projectileContainer;

    public Boss()
    {
        MoveSpeed      = 26f;
        MaxHealth      = 900f;
        AttackDamage   = 34f;
        AttackCooldown = 1.5f;
        AttackRange    = 52f;
    }

    /// <summary>Set by WaveSpawner so shells (and BossGigante's slam ring) land in the same
    /// container as every other projectile, same as Demon.SetProjectileContainer.</summary>
    public void SetProjectileContainer(Node container) => _projectileContainer = container;

    protected override float SeparationRadius  => 34f;
    protected override float BarrelAttackRange => 62f;

    protected override float NavPathDesiredDistance   => 8f;
    protected override float NavTargetDesiredDistance => 26f;
    protected override float NavRadius                => 24f;

    // Even heavier than the Ogre: a boss that could be stun-locked by knockback would trivialise
    // the fight for anyone holding a shotgun.
    protected override float KnockbackScale => 0.18f;
    protected override float KnockbackDecay => 0.55f;

    protected override Color FlashColor    => new Color(1f, 0.45f, 0.45f);
    protected override Color DeathModulate => new Color(0.3f, 0.25f, 0.25f);

    protected override int   ScoreValue       => 600;
    protected override float BloodPoolScale   => 3.0f;
    protected override float HealthDropChance => 1f; // always — the reward for surviving it

    public override void _Ready()
    {
        base._Ready();
        _baseSpeed  = MoveSpeed;
        _baseDamage = AttackDamage;
        // Announced from _Ready rather than from the spawner so it fires on every peer: each
        // machine instantiates its own copy, and each machine's HUD needs to know.
        GameManager.Instance?.NotifyBossSpawned(this);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        if (!_isHost || !IsAlive) return;

        // Only the host decides when the phase flips; clients are told. Fires exactly once
        // (guarded by !_enraged) — equivalent to the original's early-return, but no longer
        // skips the rest of the method once enraged, because UpdateSignatureAttack below has
        // to keep running in both phases.
        if (!_enraged && _currentHealth <= MaxHealth * EnragedAtHealthFraction)
        {
            if (NetworkManager.IsNetworked) Rpc(MethodName.EnterEnragedRpc);
            else EnterEnragedRpc();
        }

        UpdateSignatureAttack(delta);
    }

    /// <summary>
    /// Host-only, called every physics frame once alive (see the guard above). THE BUTCHER
    /// fires its shell star on a cooldown here; BossGigante overrides this entirely to run its
    /// ground-slam windup instead. Never runs on a client — mirroring what happened is each
    /// override's own responsibility (one RPC per shot/slam), same division of labour as
    /// Demon.FireProjectile.
    /// </summary>
    protected virtual void UpdateSignatureAttack(double delta)
    {
        _shellTimer -= (float)delta;
        if (_shellTimer > 0f) return;

        var target = GameManager.Instance?.GetNearestPlayer(GlobalPosition);
        if (target == null || !target.IsAlive) return;
        if (GlobalPosition.DistanceTo(target.GlobalPosition) > ShellRange) return;

        FireShell(target.GlobalPosition);
        _shellTimer = _enraged ? ShellCooldownEnraged : ShellCooldownNormal;
    }

    private void FireShell(Vector2 targetPos)
    {
        Vector2 dir    = (targetPos - GlobalPosition).Normalized();
        Vector2 origin = GlobalPosition + dir * 30f;
        SpawnShell(origin, dir, cosmetic: false);

        // Without this only the host would ever see the shell coming — every other host-fired
        // projectile in the game mirrors itself the same way (Demon.FireProjectile,
        // RocketLauncher.Fire).
        if (NetworkManager.IsNetworked)
            Rpc(MethodName.SpawnShellRpc, origin, dir);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    private void SpawnShellRpc(Vector2 origin, Vector2 dir) => SpawnShell(origin, dir, cosmetic: true);

    private void SpawnShell(Vector2 origin, Vector2 dir, bool cosmetic)
    {
        _shellScene ??= ResourceLoader.Load<PackedScene>("res://Scenes/Entities/BossShell.tscn");
        if (_shellScene == null) return;
        var shell = _shellScene.Instantiate<BossShell>();
        shell.Cosmetic = cosmetic; // only the host's copy's burst deals damage
        (_projectileContainer ?? GetParent())?.AddChild(shell);
        shell.Init(origin, dir);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void EnterEnragedRpc()
    {
        if (_enraged) return;
        _enraged      = true;
        MoveSpeed     = _baseSpeed  * EnragedSpeedMultiplier;
        AttackDamage  = _baseDamage * EnragedDamageMultiplier;

        // Read as angrier without needing a second sprite.
        if (_visual != null) _visual.Modulate = new Color(1f, 0.55f, 0.5f);
        AudioManager.Instance?.Play(AudioManager.EnemyDeath, 1f, 0f);
    }

    protected override void DieRpc()
    {
        // Fires on every peer because DieRpc is CallLocal — so each HUD hides its own bar.
        GameManager.Instance?.NotifyBossDefeated(this);
        base.DieRpc();
    }

    protected override void PlayDeathSound()
    {
        AudioManager.Instance?.Play(AudioManager.EnemyDeath, 1f, 0f);
        AudioManager.Instance?.Play(AudioManager.Explosion, 0.8f);
    }

    protected override void DropAmmo()
    {
        // Three packs, not one: after a fight this long the players are dry.
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/AmmoPack.tscn");
        if (scene == null) return;
        for (int i = 0; i < 3; i++)
        {
            var pack = scene.Instantiate<AmmoPack>();
            pack.AmmoAmount     = 12;
            pack.WeaponType     = ScoreManager.Instance?.GetRandomUnlockedAmmoType() ?? "Pistol";
            pack.GlobalPosition = GlobalPosition + Vector2.FromAngle(Mathf.Tau * i / 3f) * 34f;
            GetParent()?.AddChild(pack);
        }
    }

    protected override void BuildVisual()
    {
        // Canvas is 960x1050; the visible pixels span y=132..1050, putting the true visual
        // centre at (480, 591) — measured from the art's alpha bounds rather than assumed to be
        // the canvas middle, which would have floated the body above its own collision circle.
        const float scale = 0.165f;
        _visual = new Sprite2D
        {
            Texture  = ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Enemies/boss_grotesco.png"),
            Centered = false,
            Scale    = new Vector2(scale, scale),
            Position = new Vector2(-480f * scale, -591f * scale),
        };
        AddChild(_visual);

        AddChild(new ColorRect
        {
            Color    = new Color(0.15f, 0.15f, 0.15f),
            Size     = new Vector2(120, 9),
            Position = new Vector2(-60, -104)
        });

        _healthFill = new ColorRect
        {
            Color    = new Color(0.85f, 0.15f, 0.15f),
            Size     = new Vector2(120, 9),
            Position = new Vector2(-60, -104)
        };
        AddChild(_healthFill);
        _healthBarWidth  = 120f;
        _healthBarHeight = 9f;

        // Body radius scaled with the sprite so the hitbox still matches what the player sees.
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 37f } });
    }

    /// <summary>Current health as 0..1, for the HUD bar.</summary>
    public float HealthFraction => Mathf.Clamp(_currentHealth / MaxHealth, 0f, 1f);
    public bool  IsEnraged      => _enraged;
}
