using Godot;

namespace NoBoxHead;

/// <summary>
/// Single source of truth for platform-dependent behaviour. Camera, HUD and touch input all
/// branch on these, so they can never disagree about which layout is active.
/// </summary>
public static class Platform
{
    /// <summary>
    /// True on Android/iOS exports. Pass <c>-- --touch-ui</c> to force it on desktop for
    /// testing the mobile layout without exporting.
    /// </summary>
    public static bool IsMobile =>
        OS.HasFeature("mobile") ||
        System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--touch-ui") >= 0;

    /// <summary>
    /// Tabletop co-op: two players sharing one handheld laid flat on a table between them,
    /// sitting at its left and right ends (like a landscape phone/tablet split down the
    /// middle — see the reference photo). The screen still splits with a VERTICAL divider
    /// (left half / right half, same as the classic desktop split), but each half is rotated
    /// 90° so its content reads landscape-correct from that player's seat, with "up" pointing
    /// toward the centre line — i.e. away from that player, same idea as two people reading a
    /// shared menu from opposite sides of a table. Desktop/console keeps the unrotated split.
    /// </summary>
    public static bool UseTabletopCoop =>
        IsMobile && SettingsManager.Instance?.GameMode == GameMode.LocalCoop;

    /// <summary>
    /// Rotation (radians) for a player's half in tabletop co-op. The left player (index 0)
    /// rotates +90° and the right player (index 1) rotates -90° — mirrored, not the same
    /// direction, since each needs "up" to point toward the centre from their own side.
    /// Zero outside tabletop mode.
    /// </summary>
    public static float GetHalfRotation(int playerIndex)
    {
        if (!UseTabletopCoop) return 0f;
        return playerIndex == 0 ? Mathf.Pi / 2f : -Mathf.Pi / 2f;
    }
}
