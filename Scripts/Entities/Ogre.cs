using Godot;

namespace NoBoxHead;

/// <summary>
/// Mini-boss. Huge, slow, and 4x tougher than a Demon (320 HP vs 80). Spawned by WaveSpawner
/// once every OgreWaveInterval waves. Melee only — no ranged attack. Shares the same
/// navigation / stuck-recovery / barrel-breaking behaviour as Enemy, just scaled up and with
/// heavier knockback resistance befitting its size.
/// </summary>
public partial class Ogre : CharacterBody2D, IDamageable, IKnockbackable
{
	[Export] public float MoveSpeed      = 20f;  // slower than zombie (30) and demon (35)
	[Export] public float MaxHealth      = 320f; // 4x Demon's 80
	[Export] public float AttackDamage   = 28f;
	[Export] public float AttackCooldown = 1.4f;
	[Export] public float AttackRange    = 46f;  // bigger body, longer reach

	public bool IsAlive => _currentHealth > 0f;

	private float               _currentHealth;
	private float               _attackTimer;
	private Vector2             _knockback;
	private Sprite2D?           _visual;
	private ColorRect?          _healthFill;
	private bool                _isHost;
	private NavigationAgent2D?  _navAgent;

	private Vector2             _prevPosition;
	private float               _stuckTimer;
	private float               _stuckSide = 1f;
	private const float         StuckWindow   = 0.25f;
	private const float         StuckMinRatio = 0.2f;

	private Barrel?              _targetBarrel;
	private float                _barrelAttackTimer;
	private const float          BarrelAttackRange = 56f;

	public override void _Ready()
	{
		_currentHealth = MaxHealth;
		_isHost = !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
		BuildPlaceholderVisual();
		AddToGroup("enemies");

		if (_isHost)
		{
			_navAgent = new NavigationAgent2D
			{
				PathDesiredDistance   = 8f,
				TargetDesiredDistance = 24f,
				AvoidanceEnabled      = false,
				Radius                = 20f,
			};
			AddChild(_navAgent);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_isHost || !IsAlive) return;

		var target = GameManager.Instance?.GetNearestPlayer(GlobalPosition);
		if (target == null || !target.IsAlive) return;

		Vector2 dir;
		if (_navAgent != null)
		{
			_navAgent.TargetPosition = target.GlobalPosition;
			bool playerReachable = _navAgent.IsTargetReachable();

			if (!playerReachable)
				_targetBarrel = (_targetBarrel != null && IsInstanceValid(_targetBarrel) && _targetBarrel.IsAlive)
					? _targetBarrel
					: FindNearestBarrel();
			else
				_targetBarrel = null;

			Vector2 navTarget = _targetBarrel?.GlobalPosition ?? target.GlobalPosition;
			if (_targetBarrel != null) _navAgent.TargetPosition = navTarget;

			if (!_navAgent.IsNavigationFinished())
			{
				var nextPos = _navAgent.GetNextPathPosition();
				dir = (nextPos - GlobalPosition).LengthSquared() > 4f
					? (nextPos - GlobalPosition).Normalized()
					: (navTarget - GlobalPosition).Normalized();
			}
			else
			{
				dir = (navTarget - GlobalPosition).Normalized();
			}
		}
		else
		{
			dir = (target.GlobalPosition - GlobalPosition).Normalized();
		}

		Velocity = dir * MoveSpeed;

		foreach (var node in GetTree().GetNodesInGroup("enemies"))
		{
			if (node is Node2D other && other != this && IsInstanceValid(other))
			{
				float d = GlobalPosition.DistanceTo(other.GlobalPosition);
				if (d < 30f && d > 0f)
					Velocity += (GlobalPosition - other.GlobalPosition).Normalized() * (30f - d) * 0.5f;
			}
		}

		// Heavy body: knockback (applied on hit, see ApplyKnockback) is already dampened,
		// so it just needs the normal decay here.
		if (_knockback.LengthSquared() > 1f)
		{
			Velocity += _knockback;
			_knockback *= 0.6f;
		}
		else
		{
			_knockback = Vector2.Zero;
		}

		MoveAndSlide();

		float movedDist    = GlobalPosition.DistanceTo(_prevPosition);
		float expectedDist = MoveSpeed * (float)delta;
		if (expectedDist > 0f && movedDist < expectedDist * StuckMinRatio)
		{
			_stuckTimer += (float)delta;
			if (_stuckTimer >= StuckWindow)
			{
				Velocity = dir.Rotated(_stuckSide * Mathf.Pi * 0.5f) * MoveSpeed;
				MoveAndSlide();
				_stuckTimer = 0f;
				_stuckSide  = -_stuckSide;
			}
		}
		else
		{
			_stuckTimer = 0f;
		}
		_prevPosition = GlobalPosition;

		Vector2 faceDir = Velocity.LengthSquared() > 1f ? Velocity : dir;
		if (_visual != null && Mathf.Abs(faceDir.X) > 5f)
			_visual.FlipH = faceDir.X < 0f;

		_attackTimer -= (float)delta;
		if (GlobalPosition.DistanceTo(target.GlobalPosition) <= AttackRange && _attackTimer <= 0f)
		{
			target.TakeDamage(AttackDamage);
			_attackTimer = AttackCooldown;
		}

		if (_targetBarrel != null && IsInstanceValid(_targetBarrel) && _targetBarrel.IsAlive)
		{
			_barrelAttackTimer -= (float)delta;
			if (GlobalPosition.DistanceTo(_targetBarrel.GlobalPosition) <= BarrelAttackRange &&
				_barrelAttackTimer <= 0f)
			{
				_targetBarrel.TakeDamage(AttackDamage);
				_barrelAttackTimer = AttackCooldown;
			}
		}

		if (Multiplayer.HasMultiplayerPeer())
			Rpc(MethodName.SyncEnemyState, GlobalPosition, Rotation, _currentHealth);
	}

	private Barrel? FindNearestBarrel()
	{
		Barrel? nearest = null;
		float   minDist = float.MaxValue;
		foreach (var node in GetTree().GetNodesInGroup("barrels"))
		{
			if (node is not Barrel b || !IsInstanceValid(b) || !b.IsAlive) continue;
			float d = GlobalPosition.DistanceTo(b.GlobalPosition);
			if (d < minDist) { minDist = d; nearest = b; }
		}
		return nearest;
	}

	// Ogres are heavy — bullets and melee barely budge them.
	public void ApplyKnockback(Vector2 impulse) => _knockback += impulse * 0.35f;

	public void TakeDamage(float amount)
	{
		if (!_isHost || !IsAlive) return;
		_currentHealth = Mathf.Max(0f, _currentHealth - amount);
		UpdateHealthBar();
		if (Multiplayer.HasMultiplayerPeer())
			Rpc(MethodName.ApplyDamageVisualRpc, _currentHealth);
		FlashDamage();
		if (_currentHealth <= 0f)
		{
			if (Multiplayer.HasMultiplayerPeer()) Rpc(MethodName.DieRpc);
			else DieRpc();
		}
	}

	private async void FlashDamage()
	{
		Modulate = new Color(1f, 0.5f, 0.5f);
		await ToSignal(GetTree().CreateTimer(0.12), SceneTreeTimer.SignalName.Timeout);
		if (IsInstanceValid(this)) Modulate = Colors.White;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
	private void ApplyDamageVisualRpc(float newHealth)
	{
		_currentHealth = newHealth;
		UpdateHealthBar();
		FlashDamage();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	private void DieRpc()
	{
		_currentHealth = 0f;
		if (_visual != null) _visual.Modulate = new Color(0.4f, 0.4f, 0.4f);
		SetPhysicsProcess(false);
		ScoreManager.Instance?.RegisterKill(150);
		GameManager.Instance?.OnEnemyKilled();
		BloodSystem.Instance?.Pool(GlobalPosition, 2.1f);
		// Deeper, louder than a regular kill so the mini-boss death lands.
		AudioManager.Instance?.Play(AudioManager.EnemyDeath, 1f, 0f);
		AudioManager.Instance?.Play(AudioManager.Explosion, 0.5f);
		DropAmmoPack();
		// Mini-boss usually leaves food behind as a reward for the fight.
		HealthPack.TryDrop(GetParent(), GlobalPosition, 0.5f);
		CallDeferred(Node.MethodName.QueueFree);
	}

	private void DropAmmoPack()
	{
		var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Entities/AmmoPack.tscn");
		if (scene == null) return;
		var pack = scene.Instantiate<AmmoPack>();
		pack.AmmoAmount     = 10;
		pack.WeaponType     = ScoreManager.Instance?.GetRandomUnlockedAmmoType() ?? "Pistol";
		pack.GlobalPosition = GlobalPosition;
		GetParent()?.AddChild(pack);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
		 TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void SyncEnemyState(Vector2 position, float rotation, float health)
	{
		GlobalPosition = position;
		Rotation       = rotation;
		_currentHealth = health;
		UpdateHealthBar();
	}

	private void BuildPlaceholderVisual()
	{
		// Sprite's native canvas is 680x820 with the character's visual center around
		// (339, 423) — offset math below keeps that point pinned to the node's origin.
		const float scale = 0.08f;
		_visual = new Sprite2D
		{
			Texture  = ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Enemies/troll_ogro.png"),
			Centered = false,
			Scale    = new Vector2(scale, scale),
			Position = new Vector2(-339f * scale, -423.5f * scale),
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

		AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 20f } });
	}

	private void UpdateHealthBar()
	{
		if (_healthFill == null) return;
		_healthFill.Size = new Vector2(58f * (_currentHealth / MaxHealth), 6f);
	}
}
