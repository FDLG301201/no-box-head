using Godot;
using System.Collections.Generic;
using System.Linq;

namespace NoBoxHead;

/// <summary>
/// Manages either a single shared camera or a per-player split-screen layout.
/// Call Setup() once players are spawned; call RemovePlayer() on disconnect.
/// </summary>
public partial class CameraManager : Node
{
    // Shared-camera zoom limits.
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 1.0f;
    private const float ZoomLerpSpeed = 2f;
    private const float CameraLerpSpeed = 5f;
    private const float SpreadPerZoom = 600f; // pixels of player spread before zooming out

    private CameraMode _mode;
    private readonly List<Node2D> _players = new();

    // In networked play the camera follows only this peer's own player: a shared/centroid
    // camera would drift toward the midpoint between distant players and barely track you,
    // and split-screen makes no sense when each peer has its own screen.
    private Node2D? _followTarget;

    // Shared camera references.
    private Camera2D? _sharedCamera;

    // Split-screen references (one entry per player). _containers holds each player's wrapper
    // Control (see WrapForHalf) — normally just a pass-through, but in tabletop mode it's what
    // actually gets rotated/resized to fit a landscape-rendered view into a portrait-shaped
    // physical half.
    private readonly List<SubViewport> _viewports = new();
    private readonly List<Control> _containers = new();
    private readonly List<Camera2D> _splitCameras = new();

    // The Control node that holds split-screen containers.
    private Control? _screenRoot;

    // ── Setup ─────────────────────────────────────────────────────────────────

    /// <param name="localPlayer">
    /// This peer's own player. When set (networked play) the camera follows it exclusively.
    /// </param>
    public void Setup(List<Node2D> players, Control screenRoot, Node2D? localPlayer = null)
    {
        _players.Clear();
        _players.AddRange(players);
        _screenRoot = screenRoot;
        _followTarget = localPlayer;

        // Networked play still pins one camera to your own player — every peer renders its own
        // full screen, so the saved preference does not apply there.
        //
        // Everything else follows SettingsManager.CameraMode. Local co-op used to force
        // SplitScreen here and silently discard the player's choice, which is why picking
        // "Shared Camera" on desktop did nothing. Deferring to the property is safe on mobile
        // too: it already returns SplitScreen there unconditionally, because two people cannot
        // share one phone-sized view (see SettingsManager.CameraMode).
        _mode = _followTarget != null
            ? CameraMode.Shared
            : (SettingsManager.Instance?.CameraMode ?? CameraMode.Shared);

        if (_mode == CameraMode.Shared)
            SetupShared();
        else
            SetupSplitScreen();
    }

    private void SetupShared()
    {
        TeardownSplitScreen();

        // Setup() re-runs every time a player spawns (once for the host, again per remote
        // peer as their RPC arrives over the network) — reuse the existing camera instead of
        // creating a new one each time. Creating a new Camera2D without freeing the old one
        // left multiple enabled cameras fighting over "current": _sharedCamera (the field
        // UpdateSharedCamera actually moves every frame) ended up pointing at the newest one,
        // while an orphaned earlier camera silently stayed the one actually being rendered —
        // which looked exactly like "the camera isn't tracking anyone."
        if (_sharedCamera == null)
        {
            _sharedCamera = new Camera2D { Enabled = true, Zoom = Vector2.One };
            AddChild(_sharedCamera); // child of CameraManager, so inside the Arena scene tree
        }

        // Snap to the target immediately so there's no jarring lerp from world origin.
        if (_followTarget != null && IsInstanceValid(_followTarget))
        {
            _sharedCamera.GlobalPosition = _followTarget.GlobalPosition;
            return;
        }

        var validOnSetup = _players.Where(p => IsInstanceValid(p)).ToList();
        if (validOnSetup.Count > 0)
        {
            _sharedCamera.GlobalPosition = validOnSetup
                .Aggregate(Vector2.Zero, (s, p) => s + p.GlobalPosition)
                / validOnSetup.Count;
        }
    }

    private void SetupSplitScreen()
    {
        if (_sharedCamera != null)
        {
            _sharedCamera.QueueFree();
            _sharedCamera = null;
        }

        TeardownSplitScreen();
        _screenRoot!.ClipChildren = CanvasItem.ClipChildrenMode.Disabled;

        // Create a container per player, filling the screen in a grid layout.
        for (int i = 0; i < _players.Count; i++)
            AddViewportForPlayer(i);

        LayoutContainers();
    }

    private void AddViewportForPlayer(int index)
    {
        if (_screenRoot == null) return;

        // Share the main viewport's World2D so all cameras see the same game objects.
        var vp = new SubViewport
        {
            TransparentBg = false
        };
        vp.World2D = GetViewport().World2D;

        // No camera-side rotation needed: the camera renders a normal, upright landscape
        // composition; WrapForHalf below handles turning that to fit the player's seat.
        var camera = new Camera2D { Enabled = true, Zoom = Vector2.One };
        vp.AddChild(camera);

        var container = new SubViewportContainer
        {
            StretchShrink = 1,
            Stretch = true
        };
        container.AddChild(vp);

        Control wrapper;
        float rotation = Platform.GetHalfRotation(index);
        if (Mathf.IsZeroApprox(rotation))
        {
            wrapper = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            wrapper.AddChild(container);
            container.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }
        else
        {
            // Tabletop mode always splits the full device screen straight 50/50 left/right —
            // the only shape this ever needs — computed directly from the live window size
            // rather than waiting on a Resized signal from an as-yet-unparented wrapper
            // Control, which in testing never reliably fired for a nested
            // SubViewportContainer (unlike a plain Control, e.g. HUD's equivalent rotation
            // wrapper, which resizes correctly via Resized) — this sidesteps that entirely.
            var screenSize = GetViewport().GetVisibleRect().Size;
            var halfSize   = new Vector2(screenSize.X / 2f, screenSize.Y);
            wrapper = WrapForHalf(container, rotation, halfSize);
        }

        _screenRoot.AddChild(wrapper);

        _viewports.Add(vp);
        _splitCameras.Add(camera);
        _containers.Add(wrapper);
    }

    /// <summary>
    /// Wraps a SubViewportContainer, rotated 90° so a landscape-rendered view fits a
    /// portrait-shaped physical half (tabletop mode: splitting a landscape screen vertically
    /// gives each half a portrait-shaped area, but the game view inside should still read
    /// landscape from that player's seat). <paramref name="physicalSize"/> is the half's real
    /// on-screen size, known up front, so Size/Position/PivotOffset are computed once here
    /// instead of through a deferred resize callback.
    /// </summary>
    private static Control WrapForHalf(SubViewportContainer container, float rotation, Vector2 physicalSize)
    {
        var wrapper = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        wrapper.AddChild(container);

        container.Rotation = rotation;
        var swapped = new Vector2(physicalSize.Y, physicalSize.X);
        container.Size        = swapped;
        container.PivotOffset = swapped / 2f;
        // Places the pivot (Position + PivotOffset) at the half's own centre, so the rotated
        // footprint — swapped dimensions rotated 90° back to (physicalSize.X, physicalSize.Y) —
        // lands exactly on the half's bounds regardless of rotation sign.
        container.Position = physicalSize / 2f - swapped / 2f;

        return wrapper;
    }

    private void LayoutContainers()
    {
        if (_screenRoot == null) return;

        var total = _containers.Count;
        var screenSize = _screenRoot.GetViewportRect().Size;

        for (int i = 0; i < total; i++)
        {
            var c = _containers[i];
            switch (total)
            {
                case 1:
                    c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                    break;
                case 2:
                    // Left/right split — desktop, console, AND tabletop mobile co-op alike.
                    // Tabletop's left/right halves being turned 90° to read landscape from
                    // each player's seat is handled entirely inside WrapForHalf; the split
                    // itself is the same plain vertical divider either way.
                    c.AnchorLeft = i * 0.5f;
                    c.AnchorRight = (i + 1) * 0.5f;
                    c.AnchorTop = 0f;
                    c.AnchorBottom = 1f;
                    c.OffsetLeft = c.OffsetRight = c.OffsetTop = c.OffsetBottom = 0f;
                    break;
                default:
                    // 2x2 grid for 3-4 players.
                    int col = i % 2;
                    int row = i / 2;
                    c.AnchorLeft = col * 0.5f;
                    c.AnchorRight = (col + 1) * 0.5f;
                    c.AnchorTop = row * 0.5f;
                    c.AnchorBottom = (row + 1) * 0.5f;
                    c.OffsetLeft = c.OffsetRight = c.OffsetTop = c.OffsetBottom = 0f;
                    break;
            }
        }
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_mode == CameraMode.Shared)
            UpdateSharedCamera((float)delta);
        else
            UpdateSplitCameras((float)delta);
    }

    private void UpdateSharedCamera(float delta)
    {
        if (_sharedCamera == null) return;

        // Networked: track only this peer's player, at a fixed zoom.
        if (_followTarget != null)
        {
            if (!IsInstanceValid(_followTarget)) return;
            _sharedCamera.GlobalPosition = _sharedCamera.GlobalPosition
                .Lerp(_followTarget.GlobalPosition, CameraLerpSpeed * delta);
            return;
        }

        if (_players.Count == 0) return;
        var validPlayers = _players.Where(p => IsInstanceValid(p)).ToList();
        if (validPlayers.Count == 0) return;

        // Target: centroid of all players.
        var centroid = validPlayers.Aggregate(Vector2.Zero, (s, p) => s + p.GlobalPosition)
                       / validPlayers.Count;

        // Zoom: based on max pairwise distance.
        float maxDist = 0f;
        for (int i = 0; i < validPlayers.Count; i++)
            for (int j = i + 1; j < validPlayers.Count; j++)
                maxDist = Mathf.Max(maxDist, validPlayers[i].GlobalPosition
                                              .DistanceTo(validPlayers[j].GlobalPosition));

        float targetZoom = Mathf.Clamp(1f - maxDist / SpreadPerZoom, MinZoom, MaxZoom);

        _sharedCamera.GlobalPosition = _sharedCamera.GlobalPosition.Lerp(centroid, CameraLerpSpeed * delta);
        _sharedCamera.Zoom = _sharedCamera.Zoom.Lerp(Vector2.One * targetZoom, ZoomLerpSpeed * delta);
    }

    private void UpdateSplitCameras(float delta)
    {
        for (int i = 0; i < _splitCameras.Count && i < _players.Count; i++)
        {
            var cam = _splitCameras[i];
            var player = _players[i];
            if (!IsInstanceValid(player)) continue;
            cam.GlobalPosition = cam.GlobalPosition.Lerp(player.GlobalPosition, CameraLerpSpeed * delta);
        }
    }

    // ── Player management ─────────────────────────────────────────────────────

    public void AddPlayer(Node2D player)
    {
        _players.Add(player);
        if (_mode == CameraMode.SplitScreen)
        {
            AddViewportForPlayer(_players.Count - 1);
            LayoutContainers();
        }
    }

    public void RemovePlayer(Node2D player)
    {
        int idx = _players.IndexOf(player);
        if (idx < 0) return;
        _players.RemoveAt(idx);

        if (_mode == CameraMode.SplitScreen && idx < _containers.Count)
        {
            _containers[idx].QueueFree();
            _containers.RemoveAt(idx);
            _viewports.RemoveAt(idx);
            _splitCameras.RemoveAt(idx);
            LayoutContainers();
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void TeardownSplitScreen()
    {
        foreach (var c in _containers) c.QueueFree();
        _containers.Clear();
        _viewports.Clear();
        _splitCameras.Clear();
    }
}
