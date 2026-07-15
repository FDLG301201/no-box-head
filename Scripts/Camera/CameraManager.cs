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

    // Shared camera references.
    private Camera2D? _sharedCamera;

    // Split-screen references (one entry per player).
    private readonly List<SubViewport> _viewports = new();
    private readonly List<SubViewportContainer> _containers = new();
    private readonly List<Camera2D> _splitCameras = new();

    // The Control node that holds split-screen containers.
    private Control? _screenRoot;

    // ── Setup ─────────────────────────────────────────────────────────────────

    public void Setup(List<Node2D> players, Control screenRoot)
    {
        _players.Clear();
        _players.AddRange(players);
        _screenRoot = screenRoot;
        // Local co-op always needs split-screen regardless of saved camera setting.
        _mode = (SettingsManager.Instance?.GameMode == GameMode.LocalCoop)
            ? CameraMode.SplitScreen
            : (SettingsManager.Instance?.CameraMode ?? CameraMode.Shared);

        if (_mode == CameraMode.Shared)
            SetupShared();
        else
            SetupSplitScreen();
    }

    private void SetupShared()
    {
        TeardownSplitScreen();

        _sharedCamera = new Camera2D { Enabled = true, Zoom = Vector2.One };
        // Add as child of CameraManager so it's inside the Arena scene tree.
        AddChild(_sharedCamera);

        // Start at the centroid immediately so there's no jarring lerp from world origin.
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

        var camera = new Camera2D { Enabled = true, Zoom = Vector2.One };
        vp.AddChild(camera);

        var container = new SubViewportContainer
        {
            StretchShrink = 1,
            Stretch = true
        };
        container.AddChild(vp);
        _screenRoot.AddChild(container);

        _viewports.Add(vp);
        _splitCameras.Add(camera);
        _containers.Add(container);
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
                    // Horizontal split.
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
        if (_sharedCamera == null || _players.Count == 0) return;

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
