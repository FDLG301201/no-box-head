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

    public Vector2 InputVector { get; private set; } = Vector2.Zero;
    public bool IsActive { get; private set; }

    private int _touchIndex = -1;
    private Vector2 _touchOrigin; // screen-space anchor where touch began
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
            if (local.DistanceTo(_center) <= Radius)
            {
                _touchIndex = (int)ev.Index;
                _touchOrigin = local;
                IsActive = true;
                MoveThumb(Vector2.Zero);
            }
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

    private void MoveThumb(Vector2 localDelta)
    {
        float len = localDelta.Length();
        Vector2 dir = len > 0f ? localDelta / len : Vector2.Zero;
        float clamped = Mathf.Min(len, Radius);

        if (_thumb != null)
        {
            float thumbR = Radius * 0.55f;
            _thumb.Position = _center + dir * clamped - Vector2.One * (thumbR / 2f);
        }

        // DeadZone is a fraction of the stick's travel, so it scales with Radius.
        InputVector = len > DeadZone * Radius ? dir : Vector2.Zero;
    }

    private void ResetJoystick()
    {
        _touchIndex = -1;
        IsActive = false;
        InputVector = Vector2.Zero;
        if (_thumb != null)
        {
            float thumbR = Radius * 0.55f;
            _thumb.Position = _center - Vector2.One * (thumbR / 2f);
        }
    }
}
