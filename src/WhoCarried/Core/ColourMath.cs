using System.Globalization;

namespace WhoCarried.Core;

/// <summary>
/// Player colours as plain numbers: hex in and out, the recap's readability rule, HSL, and how different two colours
/// look (CIE76 ΔE). No Godot types, so the rules built on it are unit-tested.
/// </summary>
public static class ColourMath
{
    /// <summary>A colour with channels from 0 to 1, as the hex says (sRGB, no alpha).</summary>
    public readonly record struct Rgb(double R, double G, double B);

    /// <summary>What the recap needs a colour's luminance to be before it draws text or thin bars in it.</summary>
    public const double ReadableLuminance = 0.45;

    /// <summary>"d85a30", "#D85A30" or "fff"; null for anything else.</summary>
    public static Rgb? Parse(string? hex)
    {
        string h = Normalize(hex);
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return null;
        return new Rgb((v >> 16 & 0xff) / 255.0, (v >> 8 & 0xff) / 255.0, (v & 0xff) / 255.0);
    }

    /// <summary>Lower case, without a leading '#': how two colours are compared.</summary>
    public static string Normalize(string? hex) => (hex ?? "").Trim().TrimStart('#').ToLowerInvariant();

    /// <summary>Six lower-case hex digits, each channel rounded and clamped.</summary>
    public static string Hex(Rgb c) => $"{Byte(c.R):x2}{Byte(c.G):x2}{Byte(c.B):x2}";

    /// <summary>The colour as 8-bit channels would store it.</summary>
    public static Rgb Round(Rgb c) => new(Byte(c.R) / 255.0, Byte(c.G) / 255.0, Byte(c.B) / 255.0);

    private static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);

    /// <summary>Godot's <c>Color.Luminance</c>: the weighted channels, as stored.</summary>
    public static double Luminance(Rgb c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    /// <summary>
    /// A colour made readable on the dark table: mixed 15% toward white until it's bright enough, at most ten times.
    /// This is <c>RecapTheme.Accent</c>'s rule, which now calls it.
    /// </summary>
    public static Rgb Readable(Rgb c)
    {
        for (int i = 0; i < 10 && Luminance(c) < ReadableLuminance; i++)
            c = new Rgb(c.R + (1 - c.R) * 0.15, c.G + (1 - c.G) * 0.15, c.B + (1 - c.B) * 0.15);
        return c;
    }

    /// <summary>The readable version of a hex colour, as hex; a hex that can't be read comes back unchanged.</summary>
    public static string Readable(string hex) => Parse(hex) is Rgb c ? Hex(Readable(c)) : hex;

    /// <summary>Hue in degrees (0–360), saturation and lightness (0–1).</summary>
    public static (double H, double S, double L) ToHsl(Rgb c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        double l = (max + min) / 2;
        if (max == min) return (0, 0, l);
        double d = max - min;
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == c.R ? (c.G - c.B) / d + (c.G < c.B ? 6 : 0)
                 : max == c.G ? (c.B - c.R) / d + 2
                 : (c.R - c.G) / d + 4;
        return (h * 60, s, l);
    }

    public static Rgb FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360;
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double Channel(double t)
        {
            t = ((t % 1) + 1) % 1;
            return t < 1 / 6.0 ? p + (q - p) * 6 * t
                 : t < 0.5 ? q
                 : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6
                 : p;
        }
        return new Rgb(Channel(h + 1 / 3.0), Channel(h), Channel(h - 1 / 3.0));
    }

    /// <summary>The signed turn from one hue to another, -180 to 180 degrees.</summary>
    public static double HueGap(double from, double to) => ((to - from) % 360 + 540) % 360 - 180;

    /// <summary>
    /// How different two colours look (CIE76 ΔE in Lab, D65 white). Under 10 is hard to tell apart at small sizes;
    /// over 20 is clearly different.
    /// </summary>
    public static double DeltaE(Rgb a, Rgb b)
    {
        (double L, double A, double B) x = Lab(a), y = Lab(b);
        return Math.Sqrt((x.L - y.L) * (x.L - y.L) + (x.A - y.A) * (x.A - y.A) + (x.B - y.B) * (x.B - y.B));
    }

    private static (double L, double A, double B) Lab(Rgb c)
    {
        static double Linear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16 / 116.0;
        double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
        double x = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047;
        double y = r * 0.2126 + g * 0.7152 + b * 0.0722;
        double z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }
}
