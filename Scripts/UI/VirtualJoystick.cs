using Godot;

namespace NoBoxHead;

/// <summary>
/// On-screen virtual joystick for mobile touch input.
/// Works entirely in screen/viewport coordinates to avoid Control coordinate-conversion issues.
/// Reports InputVector (normalised direction) and IsActive.
/// </summary>
public partial class VirtualJoystick : Control
{
    [Export] public float Radius = 92f;
    [Export] public float DeadZone = 0.12f;

    /// <summary>
    /// How far beyond the drawn circle a touch still counts as grabbing the stick, as a
    /// multiple of Radius. The drawn circle is a target, not a hitbox: a thumb covers far more
    /// screen than the point the OS reports, and players aim at the stick with the fat part of
    /// the finger while the reported point lands somewhere near its edge. At 1.0 those touches
    /// are simply dropped and the character does not move, which is what players with larger
    /// hands were running into.
    ///
    /// Capped by the distance to the other stick, not by taste: at Radius 92 this is 147px of
    /// reach, and the two sticks' centres are 404px apart even in the narrowest co-op half, so
    /// their areas cannot both claim the same finger.
    /// </summary>
    [Export] public float TouchAreaScale = 1.6f;

    /// <summary>Touch buttons yield-list — see HUD.AddTouchButton.</summary>
    public const string TouchButtonGroup = "touch_buttons";

    public Vector2 InputVector { get; private set; } = Vector2.Zero;
    public bool IsActive { get; private set; }

    private int _touchIndex = -1;
    private Vector2 _touchOrigin; // local-space anchor where touch began
    // Where the stick is currently drawn. Equals _center at rest; while a finger is down it
    // moves to wherever that finger landed, so the stick comes to the player rather than the
    // player having to find the stick.
    private Vector2 _activeCentre;
    private ColorRect? _base;
    private ColorRect? _thumb;
    private Vector2 _center;

    public override void _Ready()
    {
        // Own the control's rect exactly (rather than trusting whatever custom_minimum_size
        // the scene declares) so GetGlobalRect() — used below for touch hit-testing — lines
        // up perfectly with the circle actually drawn. A caller places this control by its
        // top-left corner via anchors/Position, same as any other Control.
        Size   = Vector2.One * Radius * 2f;
        _center = Size / 2f;
        _activeCentre = _center;

        // Base circle (background), centred in the control's own rect.
        _base = new ColorRect
        {
            Color    = new Color(1f, 1f, 1f, 0.18f),
            Size     = Vector2.One * Radius * 2f,
            Position = _center - Vector2.One * Radius,
        };
        AddChild(_base);

        // Thumb indicator.
        float thumbR = Radius * 0.55f;
        _thumb = new ColorRect
        {
            Color    = new Color(1f, 1f, 1f, 0.45f),
            Size     = Vector2.One * thumbR,
            Position = _center - Vector2.One * (thumbR / 2f),
        };
        AddChild(_thumb);
    }

    public override void _Input(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventScreenTouch touch:
                HandleTouch(touch);
                break;
            case InputEventScreenDrag drag:
                HandleDrag(drag);
                break;
        }
    }

    /// <summary>
    /// Converts a touch position into this control's own coordinate space, accounting for
    /// rotation and the canvas layer.
    ///
    /// Everything below works in that local space, which is what makes tabletop co-op fall
    /// out for free: rotating the parent half (±90°, one direction per side — see
    /// Platform.GetHalfRotation) turns the drag vector with it, so each player's "forward"
    /// maps to their turned camera's forward, and the thumb still tracks their finger — no
    /// per-player input remapping or sign flipping needed anywhere.
    /// </summary>
    private Vector2 ToLocalPoint(Vector2 screenPoint) =>
        GetGlobalTransformWithCanvas().AffineInverse() * screenPoint;

    private void HandleTouch(InputEventScreenTouch ev)
    {
        if (ev.Pressed)
        {
            if (_touchIndex != -1) return; // already tracking a finger

            // Circle hit-test in local space. A rect test would be wrong here: Godot's
            // GetGlobalRect() ignores rotation, so it reports the wrong area once a half
            // is flipped for tabletop mode.
            var local = ToLocalPoint(ev.Position);
            if (local.DistanceTo(_center) > Radius * TouchAreaScale) return;
            if (HitsTouchButton(ev.Position)) return;

            _touchIndex  = (int)ev.Index;
            // The stick re-anchors under the finger. Without this, a touch accepted out at the
            // edge of the enlarged area would start with a large offset from _center and the
            // character would bolt off in that direction the instant it was touched.
            _activeCentre = ClampToTravel(local);
            _touchOrigin  = _activeCentre;
            IsActive = true;
            DrawStick();
            MoveThumb(Vector2.Zero);
        }
        else if ((int)ev.Index == _touchIndex)
        {
            ResetJoystick();
        }
    }

    private void HandleDrag(InputEventScreenDrag ev)
    {
        if ((int)ev.Index != _touchIndex) return;
        MoveThumb(ToLocalPoint(ev.Position) - _touchOrigin);
    }

    /// <summary>
    /// Keeps the re-anchored stick from drifting so far that half of it hangs off the screen.
    /// One Radius of travel from the rest position is enough to cover finger slop while the
    /// whole circle stays visible.
    /// </summary>
    private Vector2 ClampToTravel(Vector2 local)
    {
        var offset = local - _center;
        float len = offset.Length();
        return len <= Radius ? local : _center + offset / len * Radius;
    }

    /// <summary>
    /// True when the touch belongs to one of the on-screen buttons. The enlarged touch area
    /// overlaps them, and this class sees input before the GUI does, so without yielding here
    /// a tap on Knife or Next would ALSO grab the stick and start walking the player.
    /// </summary>
    private bool HitsTouchButton(Vector2 screenPoint)
    {
        foreach (var node in GetTree().GetNodesInGroup(TouchButtonGroup))
        {
            if (node is not Control c || !IsInstanceValid(c) || !c.IsVisibleInTree()) continue;
            // Per-button local space, so a rotated tabletop half still hit-tests correctly.
            var p = c.GetGlobalTransformWithCanvas().AffineInverse() * screenPoint;
            if (new Rect2(Vector2.Zero, c.Size).HasPoint(p)) return true;
        }
        return false;
    }

    private void DrawStick()
    {
        if (_base != null) _base.Position = _activeCentre - Vector2.One * Radius;
    }

    private void MoveThumb(Vector2 localDelta)
    {
        float len = localDelta.Length();
        Vector2 dir = len > 0f ? localDelta / len : Vector2.Zero;
        float clamped = Mathf.Min(len, Radius);

        if (_thumb != null)
        {
            float thumbR = Radius * 0.55f;
            _thumb.Position = _activeCentre + dir * clamped - Vector2.One * (thumbR / 2f);
        }

        // DeadZone is a fraction of the stick's travel, so it scales with Radius.
        InputVector = len > DeadZone * Radius ? dir : Vector2.Zero;
    }

    private void ResetJoystick()
    {
        _touchIndex = -1;
        IsActive = false;
        InputVector = Vector2.Zero;
        _activeCentre = _center;
        DrawStick();
        if (_thumb != null)
        {
            float thumbR = Radius * 0.55f;
            _thumb.Position = _center - Vector2.One * (thumbR / 2f);
        }
    }
}
