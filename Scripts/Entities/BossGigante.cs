using Godot;

namespace NoBoxHead;

/// <summary>
/// Second boss: THE COLOSSUS. Subclasses <see cref="Boss"/> so it keeps the HUD bar, the phase
/// system and the whole inherited RPC quartet, and swaps only its threat via
/// <see cref="UpdateSignatureAttack"/>.
///
/// Deliberately the OPPOSITE threat to THE BUTCHER's bullet star. The star is zone denial: it
/// punishes standing in the wrong place and rewards reading a pattern from a distance. A second
/// boss doing more of that would just be the same fight with a different sprite, so this one
/// charges — it punishes standing still at all and rewards a well-timed sidestep. Between them
/// the two bosses ask for opposite habits.
///
/// The charge is a three-beat cycle so it stays fair: WIND UP (it stops and flashes, locking in
/// a direction the player can still walk out of), CHARGE (fast, committed, in a straight line —
/// it cannot steer, so a sidestep beats it), RECOVER (a long vulnerable pause that is the
/// player's damage window). Take away any one of those beats and it becomes unreadable.
/// </summary>
public partial class BossGigante : Boss
{
    private enum Phase { Waiting, WindingUp, Charging, Recovering }

    // Wind-up is the tell. It shortens as health drops so the fight speeds up, but never below
    // ~0.35s, which is roughly the floor for a human to see it and react.
    private const float WindUpAtFullHealth = 0.9f;
    private const float WindUpAtLowHealth  = 0.38f;
    private const float ChargeSpeed        = 430f;
    private const float ChargeDuration     = 0.75f;
    private const float RecoverDuration    = 1.15f;
    private const float WaitBetweenCharges = 1.6f;
    private const float ChargeDamage       = 42f;
    private const float ChargeHitRadius    = 46f;
    private const float MinChargeRange     = 90f;  // no point charging someone already on top of it
    private const float MaxChargeRange     = 520f;

    public override string BossName => "THE COLOSSUS";

    private Phase   _phase = Phase.Waiting;
    private float   _phaseTimer = WaitBetweenCharges;
    private Vector2 _chargeDir;
    private bool    _hitThisCharge; // one hit per charge, so a slow player is not shredded

    public BossGigante()
    {
        // Slower and tougher than THE BUTCHER, because its damage arrives in single big hits
        // rather than a steady stream — it needs to be readable while it closes.
        MoveSpeed      = 22f;
        MaxHealth      = 1100f;
        AttackDamage   = 30f;
        AttackCooldown = 1.6f;
        AttackRange    = 58f;
    }

    protected override float SeparationRadius  => 40f;
    protected override float BarrelAttackRange => 70f;
    protected override float NavRadius         => 28f;

    protected override int   ScoreValue     => 750;
    protected override float BloodPoolScale => 3.4f;

    /// <summary>Wind-up shrinks linearly with health, so the pressure ramps all fight long.</summary>
    private float CurrentWindUp =>
        Mathf.Lerp(WindUpAtLowHealth, WindUpAtFullHealth, HealthFraction);

    protected override void UpdateSignatureAttack(double delta)
    {
        // Host-only, guaranteed by the caller in Boss._PhysicsProcess.
        _phaseTimer -= (float)delta;

        switch (_phase)
        {
            case Phase.Waiting:
                if (_phaseTimer > 0f) return;
                var target = GameManager.Instance?.GetNearestPlayer(GlobalPosition);
                if (target == null || !target.IsAlive) return;
                float d = GlobalPosition.DistanceTo(target.GlobalPosition);
                if (d < MinChargeRange || d > MaxChargeRange) return;

                _phase      = Phase.WindingUp;
                _phaseTimer = CurrentWindUp;
                SetTelegraph(true);
                break;

            case Phase.WindingUp:
                if (_phaseTimer > 0f) return;
                // Direction is locked HERE, at the end of the wind-up — that is what makes a
                // sidestep work. Locking it at the start would let the player walk clear during
                // the tell; steering mid-charge would make it undodgeable.
                var t2 = GameManager.Instance?.GetNearestPlayer(GlobalPosition);
                _chargeDir     = t2 != null
                    ? (t2.GlobalPosition - GlobalPosition).Normalized()
                    : Vector2.Right;
                _phase         = Phase.Charging;
                _phaseTimer    = ChargeDuration;
                _hitThisCharge = false;
                SetTelegraph(false);
                break;

            case Phase.Charging:
                // EnemyBase already ran its own MoveAndSlide this frame; this second one layers
                // the dash on top. Its 430 px/s dwarfs the 22 px/s chase underneath, so the
                // charge still reads as a straight line.
                // The charge IS this boss's attack, so the inherited melee swing is suppressed
                // for its whole duration. Without this the boss crosses into AttackRange on its
                // way in, swings, and lands the charge on the very next frame — 72 of 100 health
                // from a single telegraph (measured). Two punishments for one tell reads as
                // unfair rather than hard; the hit is big, but it is one hit.
                _attackTimer = AttackCooldown;
                Velocity = _chargeDir * ChargeSpeed;
                MoveAndSlide();
                TryChargeHit();
                if (_phaseTimer <= 0f)
                {
                    _phase      = Phase.Recovering;
                    _phaseTimer = RecoverDuration;
                }
                break;

            case Phase.Recovering:
                // Stand still and take punishment — this pause is the player's turn.
                Velocity = Vector2.Zero;
                if (_phaseTimer <= 0f)
                {
                    _phase      = Phase.Waiting;
                    _phaseTimer = WaitBetweenCharges;
                }
                break;
        }
    }

    private void TryChargeHit()
    {
        if (_hitThisCharge) return;
        foreach (var node in GetTree().GetNodesInGroup("players"))
        {
            if (node is not Player p || !IsInstanceValid(p) || !p.IsAlive) continue;
            if (GlobalPosition.DistanceTo(p.GlobalPosition) > ChargeHitRadius) continue;
            // Routed through TakeDamage so a client's player still receives it — that method
            // forwards to whoever owns the player (see Player.TakeDamage).
            p.TakeDamage(ChargeDamage);
            _hitThisCharge = true;
            return;
        }
    }

    private void SetTelegraph(bool winding)
    {
        if (NetworkManager.IsNetworked) Rpc(MethodName.TelegraphRpc, winding);
        else TelegraphRpc(winding);
    }

    /// <summary>
    /// The tell has to be visible on EVERY screen, not just the host's: a client who cannot see
    /// the wind-up has no way to dodge, which would make the fight unfair rather than hard.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void TelegraphRpc(bool winding)
    {
        if (_visual == null) return;
        _visual.Modulate = winding ? new Color(1f, 0.75f, 0.35f) : Colors.White;
    }

    protected override void PlayDeathSound()
    {
        AudioManager.Instance?.Play(AudioManager.Explosion, 1f);
        AudioManager.Instance?.Play(AudioManager.EnemyDeath, 1f, 0f);
    }

    protected override void BuildVisual()
    {
        // Canvas 990x1020, visible pixels span x=30..948 and y=120..1020, so the true visual
        // centre is (489, 570) — taken from the art's alpha bounds, not assumed to be the canvas
        // middle, which would hang the body off its own collision circle.
        const float scale = 0.17f;
        _visual = new Sprite2D
        {
            Texture  = ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Enemies/boss_gigante.png"),
            Centered = false,
            Scale    = new Vector2(scale, scale),
            Position = new Vector2(-489f * scale, -570f * scale),
        };
        AddChild(_visual);

        AddChild(new ColorRect
        {
            Color    = new Color(0.15f, 0.15f, 0.15f),
            Size     = new Vector2(130, 9),
            Position = new Vector2(-65, -112)
        });

        _healthFill = new ColorRect
        {
            Color    = new Color(0.9f, 0.55f, 0.1f),
            Size     = new Vector2(130, 9),
            Position = new Vector2(-65, -112)
        };
        AddChild(_healthFill);
        _healthBarWidth  = 130f;
        _healthBarHeight = 9f;

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 40f } });
    }
}
