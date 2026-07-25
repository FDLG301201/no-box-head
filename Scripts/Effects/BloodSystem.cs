using Godot;

namespace NoBoxHead;

/// <summary>
/// Permanent blood decals plus the transient spray that sells an impact.
///
/// Stains are stamped once into a SubViewport whose render target is never cleared, then
/// shown as a single Sprite2D. That keeps every mark on the ground for the whole match at
/// the cost of one draw call, instead of leaking a scene node per splatter.
/// </summary>
public partial class BloodSystem : Node2D
{
    public static BloodSystem? Instance { get; private set; }

    private static readonly Color BloodDark  = new(0.36f, 0.03f, 0.04f);
    private static readonly Color BloodMid   = new(0.52f, 0.05f, 0.05f);
    private static readonly Color BloodBright = new(0.68f, 0.08f, 0.08f);

    private SubViewport?  _viewport;
    private BloodPainter? _painter;

    public override void _Ready()
    {
        Instance = this;

        _viewport = new SubViewport
        {
            Size                   = new Vector2I((int)ArenaLayouts.ArenaW, (int)ArenaLayouts.ArenaH),
            TransparentBg          = true,
            Disable3D              = true,
            GuiDisableInput        = true,
            // Clear once on the first render, then never again so stains accumulate forever.
            RenderTargetClearMode  = SubViewport.ClearMode.Once,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        AddChild(_viewport);

        _painter = new BloodPainter();
        _viewport.AddChild(_painter);

        AddChild(new Sprite2D
        {
            Texture  = _viewport.GetTexture(),
            Centered = false,
        });
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Spray from a bullet/melee impact: a small stain plus droplets thrown along the hit.</summary>
    public void Splatter(Vector2 worldPos, Vector2 direction, float scale = 1f)
    {
        if (_painter == null) return;

        var dir = direction.LengthSquared() > 0.001f ? direction.Normalized() : Vector2.Right;

        _painter.Queue(worldPos, 3.5f * scale, BloodMid);

        int drops = 3 + (int)(GD.Randi() % 3);
        for (int i = 0; i < drops; i++)
        {
            float spread = (GD.Randf() - 0.5f) * 1.2f;
            float dist   = (7f + GD.Randf() * 24f) * scale;
            var   pos    = worldPos + dir.Rotated(spread) * dist;
            _painter.Queue(pos, (1.2f + GD.Randf() * 2.3f) * scale, PickShade());
        }

        Flush();
        SpawnBurst(worldPos, dir, 6, scale, 0.35f);
    }

    /// <summary>Death puddle: a wide irregular pool with droplets scattered around it.</summary>
    public void Pool(Vector2 worldPos, float scale = 1f)
    {
        if (_painter == null) return;

        // Overlapping blobs give the puddle an uneven, organic edge.
        int blobs = 5 + (int)(GD.Randi() % 4);
        for (int i = 0; i < blobs; i++)
        {
            float angle  = GD.Randf() * Mathf.Tau;
            float dist   = GD.Randf() * 7f * scale;
            var   pos    = worldPos + Vector2.FromAngle(angle) * dist;
            _painter.Queue(pos, (6f + GD.Randf() * 7f) * scale, i % 2 == 0 ? BloodDark : BloodMid);
        }

        int drops = 6 + (int)(GD.Randi() % 6);
        for (int i = 0; i < drops; i++)
        {
            float angle = GD.Randf() * Mathf.Tau;
            float dist  = (10f + GD.Randf() * 30f) * scale;
            var   pos   = worldPos + Vector2.FromAngle(angle) * dist;
            _painter.Queue(pos, (1.5f + GD.Randf() * 3f) * scale, PickShade());
        }

        Flush();
        SpawnBurst(worldPos, Vector2.Up, 14, scale, 0.5f);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    // Ask the render target for exactly one more frame, painting whatever was queued.
    private void Flush()
    {
        if (_painter == null || _viewport == null || !_painter.HasPending) return;
        _painter.QueueRedraw();
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    private static Color PickShade() => (GD.Randi() % 3) switch
    {
        0 => BloodDark,
        1 => BloodMid,
        _ => BloodBright,
    };

    // Short-lived particles so the hit reads as an impact; the stain behind them is what lasts.
    private void SpawnBurst(Vector2 worldPos, Vector2 dir, int amount, float scale, float lifetime)
    {
        var particles = new CpuParticles2D
        {
            GlobalPosition    = worldPos,
            Amount            = amount,
            Lifetime          = lifetime,
            OneShot           = true,
            Explosiveness     = 1f,
            Emitting          = true,
            ZIndex            = 3,
            Direction         = dir,
            Spread            = 55f,
            Gravity           = new Vector2(0f, 220f),
            InitialVelocityMin = 60f * scale,
            InitialVelocityMax = 190f * scale,
            ScaleAmountMin    = 1.5f * scale,
            ScaleAmountMax    = 3.5f * scale,
            Color             = BloodBright,
        };
        GetParent()?.AddChild(particles);

        // CpuParticles2D doesn't self-free after a one-shot burst.
        var timer = GetTree().CreateTimer(lifetime + 0.2f, false);
        timer.Timeout += () => { if (IsInstanceValid(particles)) particles.QueueFree(); };
    }
}
