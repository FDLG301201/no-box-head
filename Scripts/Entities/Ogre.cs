using Godot;

namespace NoBoxHead;

/// <summary>
/// Mini-boss. Huge, slow, and 4x tougher than a Demon (320 HP vs 80). Spawned by WaveSpawner
/// once every OgreWaveInterval waves. Melee only — no ranged attack. Shares the same
/// navigation / stuck-recovery / barrel-breaking behaviour as Enemy (via EnemyBase), just
/// scaled up and with heavier knockback resistance befitting its size.
/// </summary>
public partial class Ogre : EnemyBase
{
	public Ogre()
	{
		MoveSpeed      = 20f;  // slower than zombie (30) and demon (35)
		MaxHealth      = 320f; // 4x Demon's 80
        AttackDamage   = 28f;
        AttackCooldown = 1.4f;
        AttackRange    = 46f;  // bigger body, longer reach
    }

    protected override float SeparationRadius  => 30f;
    protected override float BarrelAttackRange => 56f;

    protected override float NavPathDesiredDistance   => 8f;
    protected override float NavTargetDesiredDistance => 24f;
    protected override float NavRadius                => 20f;

    // Heavy body: knockback (applied on hit, see ApplyKnockback below) is already dampened,
    // so it just needs the normal decay here.
    protected override float KnockbackDecay => 0.6f;

    // Ogres are heavy — bullets and melee barely budge them.
    protected override float KnockbackScale => 0.35f;

    protected override Color FlashColor    => new Color(1f, 0.5f, 0.5f);
    protected override float FlashDuration => 0.12f;

    protected override int   ScoreValue       => 150;
    protected override float BloodPoolScale   => 2.1f;
    // Mini-boss usually leaves food behind as a reward for the fight.
    protected override float HealthDropChance => 0.5f;

    protected override void PlayDeathSound()
    {
        // Deeper, louder than a regular kill so the mini-boss death lands.
        AudioManager.Instance?.Play(AudioManager.EnemyDeath, 1f, 0f);
        AudioManager.Instance?.Play(AudioManager.Explosion, 0.5f);
    }

    protected override void DropAmmo()
    {
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/AmmoPack.tscn");
        if (scene == null) return;
        var pack = scene.Instantiate<AmmoPack>();
        pack.AmmoAmount     = 10;
        pack.WeaponType     = ScoreManager.Instance?.GetRandomUnlockedAmmoType() ?? "Pistol";
        pack.GlobalPosition = GlobalPosition;
        GetParent()?.AddChild(pack);
    }

    protected override void BuildVisual()
    {
		// Canvas 706x630, character's visual centre (357, 318) — taken from the art's ALPHA BOUNDS, not the
		// canvas middle, so the body stays pinned to the node origin where the collision circle
		// and pathing live. Both numbers are derived, never eyeballed: after any art re-export run
		// `python Tools/sprite_metrics.py emit` and paste what it prints.
		const float scale = 0.10667f;
		_visual = new Sprite2D
		{
			Texture  = ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Enemies/troll_ogro.png"),
			Centered = false,
			Scale    = new Vector2(scale, scale),
			Position = new Vector2(-357f * scale, -318f * scale),
		};
		AddChild(_visual);

		AddChild(new ColorRect
		{
			Color    = new Color(0.2f, 0.2f, 0.2f),
			Size     = new Vector2(58, 6),
			Position = new Vector2(-29, -40)
		});

		_healthFill = new ColorRect
		{
			Color    = new Color(0.1f, 0.8f, 0.2f),
			Size     = new Vector2(58, 6),
			Position = new Vector2(-29, -40)
		};
		AddChild(_healthFill);
		_healthBarWidth  = 58f;
		_healthBarHeight = 6f;

		AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 20f } });
	}
}
