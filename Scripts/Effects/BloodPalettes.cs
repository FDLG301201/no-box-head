using Godot;

namespace NoBoxHead;

/// <summary>
/// Fixed set of blood colour presets for the Settings screen. A small fixed list (rather than a
/// colour wheel) is deliberate — it has to be tappable with a thumb on a phone.
/// </summary>
public static class BloodPalettes
{
    public readonly struct Shades
    {
        public readonly Color Dark, Mid, Bright;
        public Shades(Color dark, Color mid, Color bright) { Dark = dark; Mid = mid; Bright = bright; }
    }

    public static readonly string[] Names = { "Red", "Green", "Purple", "Black" };

    // Index 0 (Red) is the ORIGINAL hardcoded triple from before this setting existed, kept
    // byte-for-byte so existing players see no change at default settings. It is not run
    // through Derive() below because Derive's uniform scaling can't reproduce these three
    // colours exactly (the original dark/mid/bright were hand-tuned per channel, not a single
    // base colour scaled up and down).
    private static readonly Shades Red = new(
        new Color(0.36f, 0.03f, 0.04f),
        new Color(0.52f, 0.05f, 0.05f),
        new Color(0.68f, 0.08f, 0.08f));

    // Other presets are derived from one base ("mid") colour so each still has the same
    // dark/mid/bright shading depth as red, instead of flattening to a single flat tone.
    private static readonly Shades Green  = Derive(new Color(0.06f, 0.42f, 0.07f));
    private static readonly Shades Purple = Derive(new Color(0.34f, 0.06f, 0.46f));
    private static readonly Shades Black  = Derive(new Color(0.10f, 0.10f, 0.11f));

    private static readonly Shades[] All = { Red, Green, Purple, Black };

    /// <summary>The palette selected in Settings, defaulting to Red (index 0) if unset.</summary>
    public static Shades Current()
    {
        int index = SettingsManager.Instance?.BloodColorIndex ?? 0;
        return All[Mathf.Clamp(index, 0, All.Length - 1)];
    }

    // Scale a base colour darker/brighter by a flat factor per channel. Roughly matches the
    // spread between the original red constants (dark ~0.7x mid, bright ~1.4x mid).
    private static Shades Derive(Color mid) =>
        new(Scale(mid, 0.7f), mid, Scale(mid, 1.4f));

    private static Color Scale(Color c, float factor) => new(
        Mathf.Clamp(c.R * factor, 0f, 1f),
        Mathf.Clamp(c.G * factor, 0f, 1f),
        Mathf.Clamp(c.B * factor, 0f, 1f));
}
