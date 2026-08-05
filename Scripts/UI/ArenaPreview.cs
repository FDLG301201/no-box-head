using Godot;

namespace NoBoxHead;

/// <summary>
/// Schematic of an arena for the menu's world picker, drawn straight from ArenaLayouts instead
/// of from authored bitmaps — a world added there shows up here automatically, and the preview
/// cannot drift out of sync with the layout the game actually builds.
/// </summary>
public partial class ArenaPreview : Control
{
    private ArenaType _type = ArenaType.Classic;
    private ulong     _seed;

    public void SetArena(ArenaType type, ulong seed)
    {
        _type = type;
        _seed = seed;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Size.X <= 0f || Size.Y <= 0f) return;

        // Letterbox the 1280x720 playfield into whatever box the menu allotted, so proportions
        // survive and a wide obstacle still reads as wide.
        float scale  = Mathf.Min(Size.X / ArenaLayouts.ArenaW, Size.Y / ArenaLayouts.ArenaH);
        var   drawn  = new Vector2(ArenaLayouts.ArenaW, ArenaLayouts.ArenaH) * scale;
        var   origin = (Size - drawn) / 2f;

        DrawRect(new Rect2(origin, drawn), ArenaLayouts.FloorColor(_type));

        var obstacleColor = ArenaLayouts.ObstacleColor(_type);
        foreach (var (center, size) in ArenaLayouts.GetObstacles(_type, _seed))
            DrawRect(new Rect2(origin + (center - size / 2f) * scale, size * scale), obstacleColor);

        DrawRect(new Rect2(origin, drawn), new Color(0.45f, 0.45f, 0.5f), filled: false, width: 2f);
    }
}
