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

    // WeaponType (a WeaponName) → ammo pickup sprite.
    private static readonly Dictionary<string, string> AmmoSprites = new()
    {
        { "Pistol",      "res://Assets/Sprites/Ammo/pistol.png"     },
        { "Shotgun",     "res://Assets/Sprites/Ammo/shotgun.png"    },
        { "Machine Gun", "res://Assets/Sprites/Ammo/machinegun.png" },
        { "Barrel",      "res://Assets/Sprites/Ammo/barrel.png"     },
        { "Grenade",     "res://Assets/Sprites/Ammo/grenade.png"    },
    };

    public override void _Ready()
    {
        CollisionLayer = 8;
        CollisionMask  = 2;
        Monitoring     = true;
        Monitorable    = true;

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 14f } });

        var path = AmmoSprites.GetValueOrDefault(WeaponType, AmmoSprites["Pistol"]);
        AddChild(new Sprite2D
        {
            Texture  = ResourceLoader.Load<Texture2D>(path),
            Centered = true,
            Scale    = new Vector2(0.62f, 0.62f),
        });

        // "+N" tag sits just below the icon so it doesn't cover it.
        var lbl = new Label
        {
            Text                = $"+{AmmoAmount}",
            Position            = new Vector2(-14, 10),
            Size                = new Vector2(28, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        lbl.AddThemeFontSizeOverride("font_size", 10);
        lbl.AddThemeColorOverride("font_color", Colors.White);
        lbl.AddThemeColorOverride("font_outline_color", Colors.Black);
        lbl.AddThemeConstantOverride("outline_size", 4);
        AddChild(lbl);

        BodyEntered += OnBodyEntered;
        Bob();
    }

    // Gentle hover so pickups read as collectible, matching the health pack.
    private void Bob()
    {
        var tween = CreateTween().SetLoops();
        tween.TweenProperty(this, "position:y", Position.Y - 3f, 0.7)
             .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(this, "position:y", Position.Y + 3f, 0.7)
             .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private void OnBodyEntered(Node2D body)
    {
        if (_pickedUp || body is not Player player) return;
        _pickedUp = true;
        player.AddAmmo(AmmoAmount, WeaponType);
        AudioManager.Instance?.Play(AudioManager.PickupAmmo, 0.7f);
        QueueFree();
    }
}
