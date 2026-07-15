using Godot;

namespace NoBoxHead;

public interface IKnockbackable
{
    void ApplyKnockback(Vector2 impulse);
}
