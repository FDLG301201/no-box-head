using Godot;
using System.Collections.Generic;

namespace NoBoxHead;

/// <summary>
/// Pickup that grants reserve ammo to the matching weapon on the player who walks over it.
/// WeaponType must match the weapon's WeaponName ("Pistol", "Shotgun", "Machine Gun").
/// </summary>
public partial class AmmoPack : Area2D
{
    [Export] public int    AmmoAmount = 12;
    [Export] public string WeaponType = "Pistol";

    private bool _pickedUp;

    private static readonly Dictionary<string, Color> PackColors = new()
    {
        { "Pistol",      new Color(1f,    0.85f, 0.1f) },
        { "Shotgun",     new Color(0.95f, 0.2f,  0.2f) },
        { "Machine Gun", new Color(0.2f,  0.85f, 0.2f) },
    };

    public override void _Ready()
    {
        CollisionLayer = 8;
        CollisionMask  = 2;
        Monitoring     = true;
        Monitorable    = true;

        var color = PackColors.TryGetValue(WeaponType, out var c) ? c : PackColors["Pistol"];

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 14f } });
        AddChild(new ColorRect
        {
            Color    = color,
            Size     = new Vector2(20, 12),
            Position = new Vector2(-10, -6)
        });

        var lbl = new Label { Text = $"+{AmmoAmount}", Position = new Vector2(-10, -7) };
        lbl.AddThemeFontSizeOverride("font_size", 10);
        lbl.AddThemeColorOverride("font_color", Colors.Black);
        AddChild(lbl);

        BodyEntered += OnBodyEntered;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (_pickedUp || body is not Player player) return;
        _pickedUp = true;
        player.AddAmmo(AmmoAmount, WeaponType);
        QueueFree();
    }
}
