using Godot;

namespace NoBoxHead;

/// <summary>
/// Makes a crowd of enemies behave like a crowd rather than a wall: a player walking into them
/// pushes through, and the bodies in the way slide aside and then close back in.
///
/// This is deliberately NOT done with collision layers. CharacterBody2D collision is all or
/// nothing — either the player is blocked outright, or (if only the enemy masks the player)
/// MoveAndSlide's depenetration expels the enemy every frame and a walking player bulldozes it
/// along at a fixed offset, which is what previously read as zombies being glued on. Both
/// bodies therefore mask walls only, and the yielding is an explicit velocity term instead:
/// gradual, capped by how deep the overlap actually is, and applied to the enemy's own
/// movement, so it steps out of the way under its own power and resumes chasing afterwards.
/// </summary>
public static class CrowdSeparation
{
    // Push speed per pixel of overlap. Tuned so being walked straight through clears a body out
    // of the way at a good fraction of the player's 160 px/s, while a glancing brush barely
    // nudges it — the difference between shouldering past someone and knocking them flying.
    private const float PushStrength = 4f;

    /// <summary>
    /// Velocity to add so <paramref name="self"/> yields to any player standing inside
    /// <paramref name="contactRadius"/> (normally the two bodies' radii summed).
    /// </summary>
    public static Vector2 AwayFromPlayers(Node2D self, float contactRadius)
    {
        var push = Vector2.Zero;
        foreach (var node in self.GetTree().GetNodesInGroup("players"))
        {
            if (node is not Node2D player || !GodotObject.IsInstanceValid(player)) continue;

            Vector2 away = self.GlobalPosition - player.GlobalPosition;
            float   dist = away.Length();
            // Dead-centre overlap has no meaningful direction to push along; the player is
            // moving, so it resolves itself on the next frame.
            if (dist >= contactRadius || dist <= 0.01f) continue;

            push += away / dist * (contactRadius - dist) * PushStrength;
        }
        return push;
    }
}
