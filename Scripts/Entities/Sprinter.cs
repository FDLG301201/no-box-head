using Godot;

namespace NoBoxHead;

/// <summary>
/// Fast, fragile zombie variant. 1.5x a regular zombie's speed but half its health — dies in
/// a couple of hits but closes distance quickly and punishes standing still. Shares Enemy's
/// navigation / stuck-recovery / barrel-breaking behaviour via EnemyBase.
/// </summary>
public partial class Sprinter : EnemyBase
{
	public Sprinter()
	{
		MoveSpeed      = 60f; // 2x Enemy's 30
		MaxHealth      = 15f; // half Enemy's 30
		AttackDamage   = 6f;
		AttackCooldown = 0.7f;
		AttackRange    = 26f;
	}

	protected override float StuckWindow => 0.2f; // fast mover — recover from clips quicker

	protected override float SeparationRadius  => 20f;
	protected override float BarrelAttackRange => 36f;

	protected override float NavPathDesiredDistance   => 6f;
	protected override float NavTargetDesiredDistance => 18f;
	protected override float NavRadius                => 9f;

	protected override float FlashDuration => 0.1f;

	protected override int   ScoreValue       => 8;
	protected override float BloodPoolScale   => 0.8f;
	protected override float HealthDropChance => 0.025f;

	protected override void PlayDeathSound() =>
		AudioManager.Instance?.Play(AudioManager.EnemyDeath, 0.6f, 0.18f);

	protected override void BuildVisual()
	{
		// Canvas 360x372, character's visual centre (192.5, 186) — taken from the art's ALPHA BOUNDS, not the
		// canvas middle, so the body stays pinned to the node origin where the collision circle
		// and pathing live. Both numbers are derived, never eyeballed: after any art re-export run
		// `python Tools/sprite_metrics.py emit` and paste what it prints.
		const float scale = 0.08566f;
		_visual = new Sprite2D
		{
			Texture  = ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Enemies/runner_amarillo.png"),
			Centered = false,
			Scale    = new Vector2(scale, scale),
			Position = new Vector2(-192.5f * scale, -186f * scale),
		};
		AddChild(_visual);

		AddChild(new ColorRect
		{
			Color    = new Color(0.2f, 0.2f, 0.2f),
			Size     = new Vector2(24, 4),
			Position = new Vector2(-12, -19)
		});

		_healthFill = new ColorRect
		{
			Color    = new Color(0.9f, 0.2f, 0.2f),
			Size     = new Vector2(24, 4),
			Position = new Vector2(-12, -19)
		};
		AddChild(_healthFill);
		_healthBarWidth  = 24f;
		_healthBarHeight = 4f;

		AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 9f } });
	}
}
