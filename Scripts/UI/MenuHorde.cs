using Godot;

namespace NoBoxHead;

/// <summary>
/// Ambient background for the main menu: a thin horde that shambles toward the pointer (mouse on
/// desktop, finger on mobile). Purely decorative — it never reads input as "handled" and never
/// participates in layout.
///
/// Two deliberate choices:
///
/// A Node2D, not a Control. Anything in the menu's VBox contributes to the content height, and
/// this menu is already fighting for vertical room — at one point the content stood 691px tall
/// and the bottom buttons fell off a 531px window. A Node2D sits outside the layout entirely, so
/// it cannot push a button off-screen no matter what it does. It also never consumes a click,
/// because it is not part of the GUI input chain at all — safer than relying on MouseFilter.
///
/// Sprites are drawn Centered, unlike every in-game character. The game pins each sprite by a
/// hand-measured alpha-bounds centre so the body lines up with its collision circle; there is no
/// collision here, and hard-coding those constants would silently break this file the next time
/// the art is re-exported. Decoration should not be coupled to gameplay tuning.
/// </summary>
public partial class MenuHorde : Node2D
{
    // Small enough to stay cheap on a phone, dense enough to read as a crowd rather than a
    // handful of strays.
    private const int   Count        = 14;
    private const float MinSpeed     = 14f;
    private const float MaxSpeed     = 34f;
    private const float MinScale     = 0.055f;
    private const float MaxScale     = 0.085f;
    // Recycled once they reach the pointer, otherwise the whole horde piles onto the cursor
    // within a few seconds and the rest of the screen empties out.
    private const float ArrivalRadius = 46f;
    private const float BobPixels     = 1.8f;
    private const float BobSpeed      = 3.2f;
    private const float Alpha         = 0.17f;

    private readonly Sprite2D[] _sprites = new Sprite2D[Count];
    private readonly float[]    _speeds  = new float[Count];
    private readonly float[]    _phases  = new float[Count];
    private readonly float[]    _baseY   = new float[Count];

    // Touch does not move the mouse cursor on every device, so the last touch is tracked
    // explicitly rather than trusting GetGlobalMousePosition() alone.
    private Vector2 _target;
    private bool    _hasTouch;

    public override void _Ready()
    {
        var texture = ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Enemies/zombie.png");
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        var size = GetViewportRect().Size;
        _target = size * 0.5f;

        for (int i = 0; i < Count; i++)
        {
            float scale = rng.RandfRange(MinScale, MaxScale);
            var s = new Sprite2D
            {
                Texture  = texture,
                Centered = true,
                Scale    = new Vector2(scale, scale),
                // Barely visible: this sits behind the title and five buttons, and the menu has
                // to stay readable. It is atmosphere, not a focal point.
                Modulate = new Color(1f, 1f, 1f, Alpha),
                // Smaller ones are further away, so they draw behind the larger ones.
                ZIndex   = Mathf.RoundToInt(scale * 1000f),
            };
            AddChild(s);
            _sprites[i] = s;
            _speeds[i]  = rng.RandfRange(MinSpeed, MaxSpeed);
            _phases[i]  = rng.RandfRange(0f, Mathf.Tau);
            Respawn(i, rng, size, spreadInside: true);
        }
        _rng = rng;
    }

    private RandomNumberGenerator _rng = null!;

    /// <summary>
    /// Places one walker. On first build they are scattered across the whole screen so the menu
    /// opens with a crowd already present; afterwards they re-enter from just outside an edge, so
    /// the horde reads as a continuous stream rather than popping into existence mid-screen.
    /// </summary>
    private void Respawn(int i, RandomNumberGenerator rng, Vector2 size, bool spreadInside)
    {
        Vector2 pos;
        if (spreadInside)
        {
            pos = new Vector2(rng.RandfRange(0f, size.X), rng.RandfRange(0f, size.Y));
        }
        else
        {
            const float margin = 70f;
            pos = rng.RandiRange(0, 3) switch
            {
                0 => new Vector2(rng.RandfRange(0f, size.X), -margin),
                1 => new Vector2(rng.RandfRange(0f, size.X), size.Y + margin),
                2 => new Vector2(-margin, rng.RandfRange(0f, size.Y)),
                _ => new Vector2(size.X + margin, rng.RandfRange(0f, size.Y)),
            };
        }
        _sprites[i].Position = pos;
        _baseY[i] = pos.Y;
    }

    public override void _Input(InputEvent @event)
    {
        // Read only — the event is never marked handled, so buttons underneath still receive it.
        if (@event is InputEventScreenTouch { Pressed: true } touch)
        {
            _target = touch.Position;
            _hasTouch = true;
        }
        else if (@event is InputEventScreenDrag drag)
        {
            _target = drag.Position;
            _hasTouch = true;
        }
    }

    public override void _Process(double delta)
    {
        var size = GetViewportRect().Size;
        if (!_hasTouch)
        {
            var m = GetViewport().GetMousePosition();
            // A mouse parked outside the window reports a stale position; falling back to the
            // centre keeps the horde converging on something sensible instead of a corner.
            _target = GetViewportRect().HasPoint(m) ? m : size * 0.5f;
        }

        float dt = (float)delta;
        for (int i = 0; i < Count; i++)
        {
            var s = _sprites[i];
            Vector2 toTarget = _target - new Vector2(s.Position.X, _baseY[i]);

            if (toTarget.Length() <= ArrivalRadius)
            {
                Respawn(i, _rng, size, spreadInside: false);
                continue;
            }

            Vector2 step = toTarget.Normalized() * _speeds[i] * dt;
            _baseY[i] += step.Y;

            // Mirror rather than rotate — same fixed front-facing convention the in-game
            // characters use, so the menu reads as the same game.
            if (Mathf.Abs(step.X) > 0.01f) s.FlipH = step.X < 0f;

            _phases[i] += dt * BobSpeed;
            s.Position = new Vector2(
                s.Position.X + step.X,
                _baseY[i] - Mathf.Abs(Mathf.Sin(_phases[i])) * BobPixels);
        }
    }
}
