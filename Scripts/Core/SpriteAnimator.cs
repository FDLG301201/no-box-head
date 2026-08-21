using Godot;

namespace NoBoxHead;

/// <summary>
/// Gives a single-frame sprite a sense of motion without any animation frames: a walk bob with
/// squash-and-stretch while moving, and a slow breath while standing still.
///
/// Why procedural rather than a sprite sheet: the art is one static pose per character (four
/// enemies, six player skins) and there is no character-sprite generator, so real frames would
/// mean drawing five walk cycles. At the size these render on screen (~37x45 px) a bob plus a
/// little squash reads as "alive" convincingly, for a fraction of the work.
///
/// Speed is derived from how far the owner ACTUALLY moved since the last frame, not from its
/// Velocity. That matters for multiplayer: enemies only run their movement code on the host, and
/// clients receive positions over RPC — reading Velocity would leave every enemy frozen on a
/// client. Observed motion is identical on every peer and costs nothing to replicate.
/// </summary>
public sealed class SpriteAnimator
{
    private readonly Node2D   _owner;
    private readonly Sprite2D _sprite;
    private readonly Vector2  _restPosition;
    private readonly Vector2  _restScale;
    private readonly float    _referenceSpeed;

    private Vector2 _lastOwnerPosition;
    private float   _phase;
    private float   _moveBlend; // 0 = idle, 1 = walking; smoothed to avoid popping

    // Two steps per full sine cycle, so one cycle is a left-right stride.
    private const float StepsPerSecondAtFullSpeed = 3.4f;
    // Whole pixels matter at this render size: 2px of lift is clearly readable, more starts to
    // look like the character is jumping rather than walking.
    private const float BobPixels        = 2f;
    private const float IdleDriftPixels  = 0.7f;
    private const float BreathSpeed      = 1.7f;
    private const float BlendRate        = 8f;

    /// <param name="referenceSpeed">
    /// The owner's normal top speed. The cycle runs at full rate at this speed and slower below
    /// it, so a Sprinter's legs visibly churn faster than an Ogre's.
    /// </param>
    public SpriteAnimator(Node2D owner, Sprite2D sprite, float referenceSpeed)
    {
        _owner          = owner;
        _sprite         = sprite;
        _restPosition   = sprite.Position;
        _restScale      = sprite.Scale;
        _referenceSpeed = Mathf.Max(referenceSpeed, 1f);
        _lastOwnerPosition = owner.GlobalPosition;
    }

    public void Update(float delta)
    {
        if (delta <= 0f) return;

        float speed = _owner.GlobalPosition.DistanceTo(_lastOwnerPosition) / delta;
        _lastOwnerPosition = _owner.GlobalPosition;

        // Ignore the teleport-sized jumps a client sees when a position RPC arrives after a
        // hitch; without this the sprite would spasm on every late packet.
        if (speed > _referenceSpeed * 6f) speed = 0f;

        float target = Mathf.Clamp(speed / _referenceSpeed, 0f, 1.4f);
        _moveBlend = Mathf.Lerp(_moveBlend, target, Mathf.Min(1f, BlendRate * delta));

        _phase += delta * Mathf.Tau * StepsPerSecondAtFullSpeed * Mathf.Max(_moveBlend, 0.0001f);

        // POSITION ONLY — the scale is never touched. Squash-and-stretch was tried first and had
        // to be removed: these characters render about 36px wide, so even a 4% horizontal squash
        // is barely one pixel, and resampling the texture at that size visibly ate thin features
        // like the zombies' arms. Moving the sprite leaves the texture sampled exactly as
        // authored on every frame, so nothing can be shaved off.
        float walk = Mathf.Sin(_phase);

        // Walking: a two-beat hop, so each stride lifts the body once.
        float bob = -Mathf.Abs(walk) * BobPixels * _moveBlend;

        // Idle: a slow vertical drift instead of a breath-scale, for the same reason.
        float idle = Mathf.Sin(Time.GetTicksMsec() / 1000f * BreathSpeed)
                     * IdleDriftPixels * (1f - _moveBlend);

        _sprite.Scale    = _restScale;
        _sprite.Position = new Vector2(_restPosition.X, _restPosition.Y + bob + idle);
    }
}
