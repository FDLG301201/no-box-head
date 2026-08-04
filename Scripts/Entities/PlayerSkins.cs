using Godot;

namespace NoBoxHead;

/// <summary>
/// Catalog of selectable player appearances. Textures are generated from the base sprite by
/// Tools/generate_player_skins.py — adding a skin means adding it there and here.
/// </summary>
public static class PlayerSkins
{
    public readonly record struct Skin(string Name, string Path, Color Swatch);

    public static readonly Skin[] All =
    {
        new("Soldier", "res://Assets/Sprites/Player/skin_soldier.png", new Color(0.36f, 0.42f, 0.29f)),
        new("Blue",    "res://Assets/Sprites/Player/skin_blue.png",    new Color(0.23f, 0.36f, 0.59f)),
        new("Crimson", "res://Assets/Sprites/Player/skin_crimson.png", new Color(0.64f, 0.19f, 0.19f)),
        new("Violet",  "res://Assets/Sprites/Player/skin_violet.png",  new Color(0.43f, 0.27f, 0.60f)),
        new("Teal",    "res://Assets/Sprites/Player/skin_teal.png",    new Color(0.19f, 0.51f, 0.49f)),
        new("Orange",  "res://Assets/Sprites/Player/skin_orange.png",  new Color(0.78f, 0.43f, 0.16f)),
    };

    public static int Count => All.Length;

    public static Skin Get(int index) => All[((index % Count) + Count) % Count];

    /// <summary>
    /// Skin for a given player slot. Player 0 uses the chosen skin; the others are offset from
    /// it so everyone on screen stays visually distinct without extra configuration.
    /// </summary>
    public static Skin ForPlayer(int playerIndex)
    {
        int chosen = SettingsManager.Instance?.SkinIndex ?? 0;
        return Get(chosen + playerIndex);
    }

    public static Texture2D? LoadTexture(int index) =>
        ResourceLoader.Load<Texture2D>(Get(index).Path);
}
