using Godot;

namespace NoBoxHead;

/// <summary>
/// On-screen virtual joystick for mobile touch input.
/// Works entirely in screen/viewport coordinates to avoid Control coordinate-conversion issues.
/// Reports InputVector (normalised direction) and IsActive.
/// </summary>
public partial class VirtualJoystick : Control
{
    [Export] public float Radius = 60f;
    [Export] public float DeadZone = 0.1f;

    public Vector2 InputVector { get; private set; } = Vector2.Zero;
    public bool IsActive { get; private set; }

    private int _touchIndex = -1;
    private Vector2 _touchOrigin; // screen-space anchor where touch began
    private ColorRect? _base;
    private ColorRect? _thumb;

    public override void _Ready()
    {
        // Base circle (background).
        _base = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0.18f),
            Size = Vector2.One * Radius * 2f,
            Position = Vector2.One * (-Radius) // centered on control's (0,0)
        };
        AddChild(_base);

        // Thumb indicator.
        float thumbR = Radius * 0.55f;
        _thumb = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0.45f),
            Size = Vector2.One * thumbR,
            Position = Vector2.One * (-thumbR / 2f)
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

    private void HandleTouch(InputEventScreenTouch ev)
    {
        if (ev.Pressed)
        {
            if (_touchIndex != -1) return; // already tracking a finger

            // GetGlobalRect() returns the Control's bounding Rect2 in viewport/screen space.
            if (GetGlobalRect().HasPoint(ev.Position))
            {
                _touchIndex = (int)ev.Index;
                _touchOrigin = ev.Position;
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
        // Offset in screen space from where the touch started.
        Vector2 delta = ev.Position - _touchOrigin;
        MoveThumb(delta);
    }

    private void MoveThumb(Vector2 screenDelta)
    {
        float len = screenDelta.Length();
        Vector2 dir = len > 0f ? screenDelta / len : Vector2.Zero;
        float clamped = Mathf.Min(len, Radius);

        // Position thumb visually inside the control (local space = screen space here).
        if (_thumb != null)
        {
            float thumbR = Radius * 0.55f;
            _thumb.Position = dir * clamped + Vector2.One * (-thumbR / 2f);
        }

        InputVector = len > DeadZone ? dir : Vector2.Zero;
    }

    private void ResetJoystick()
    {
        _touchIndex = -1;
        IsActive = false;
        InputVector = Vector2.Zero;
        if (_thumb != null)
        {
            float thumbR = Radius * 0.55f;
            _thumb.Position = Vector2.One * (-thumbR / 2f);
        }
    }
}
