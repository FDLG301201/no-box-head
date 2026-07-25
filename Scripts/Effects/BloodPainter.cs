using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

/// <summary>
/// Lives inside BloodSystem's render target and stamps queued blobs onto it. Because the
/// target is never cleared, each _Draw only needs to paint the marks added since the last
/// frame — everything painted before is already baked into the texture.
/// </summary>
public partial class BloodPainter : Node2D
{
    private readonly List<(Vector2 Pos, float Radius, Color Color)> _pending = new();

    public bool HasPending => _pending.Count > 0;

    public void Queue(Vector2 pos, float radius, Color color) =>
        _pending.Add((pos, radius, color));

    public override void _Draw()
    {
        foreach (var (pos, radius, color) in _pending)
            DrawCircle(pos, radius, color);
        _pending.Clear();
    }
}
