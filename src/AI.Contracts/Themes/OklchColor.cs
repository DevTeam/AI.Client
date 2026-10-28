namespace AI.Contracts.Themes;

using System.Globalization;

/// <summary>
/// A colour in Oklch: perceptual lightness, chroma and hue. User themes are built in this space
/// because a step in lightness reads as an even step to the eye, which a step in sRGB does not; the
/// palette ramps in app.css were measured in it too, so a generated theme can be compared with a
/// preset by lightness alone.
/// </summary>
public readonly record struct OklchColor(double Lightness, double Chroma, double Hue)
{
    /// <summary>Lightness of pure black and of pure white.</summary>
    public const double MinLightness = 0;
    public const double MaxLightness = 1;

    public OklchColor WithLightness(double lightness) => this with { Lightness = lightness };

    public OklchColor WithChroma(double chroma) => this with { Chroma = chroma };

    /// <summary>The same colour with its lightness clamped to the Oklch range, which every ramp step passes through.</summary>
    public OklchColor Clamped => this with { Lightness = Math.Clamp(Lightness, MinLightness, MaxLightness) };

    /// <summary>Whether a monitor can show this colour as it is, without reducing its chroma.</summary>
    public bool IsInGamut => SrgbColor.TryFromOklch(Clamped, out _);

    /// <summary>
    /// The same colour with its chroma lowered until it fits in sRGB, which keeps the hue and the
    /// lightness the caller asked for. A theme seed may name a saturated colour that no screen can
    /// show at that lightness.
    /// </summary>
    public OklchColor InGamut()
    {
        var clamped = Clamped;
        if (clamped.Chroma <= 0) return clamped with { Chroma = 0 };
        if (clamped.IsInGamut) return clamped;
        var low = 0.0;
        var high = clamped.Chroma;
        for (var step = 0; step < 24; step++)
        {
            var middle = (low + high) / 2;
            if ((clamped with { Chroma = middle }).IsInGamut) low = middle;
            else high = middle;
        }
        return clamped with { Chroma = low };
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"oklch({Lightness:0.###} {Chroma:0.####} {Hue:0.#})");
}

/// <summary>An sRGB colour with 8 bits per channel, which is what the page's tokens are written in.</summary>
public readonly record struct SrgbColor(byte Red, byte Green, byte Blue)
{
    public double RelativeLuminance
    {
        get
        {
            var red = ToLinear(Red);
            var green = ToLinear(Green);
            var blue = ToLinear(Blue);
            return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
        }
    }

    /// <summary>The WCAG contrast ratio between two colours, from 1 to 21.</summary>
    public static double ContrastRatio(SrgbColor first, SrgbColor second)
    {
        var one = first.RelativeLuminance;
        var other = second.RelativeLuminance;
        return (Math.Max(one, other) + 0.05) / (Math.Min(one, other) + 0.05);
    }

    public OklchColor ToOklch()
    {
        var red = ToLinear(Red);
        var green = ToLinear(Green);
        var blue = ToLinear(Blue);

        var l = 0.4122214708 * red + 0.5363325363 * green + 0.0514459929 * blue;
        var m = 0.2119034982 * red + 0.6806995451 * green + 0.1073969566 * blue;
        var s = 0.0883024619 * red + 0.2817188376 * green + 0.6299787005 * blue;

        var lRoot = Math.Cbrt(l);
        var mRoot = Math.Cbrt(m);
        var sRoot = Math.Cbrt(s);

        var lightness = 0.2104542553 * lRoot + 0.7936177850 * mRoot - 0.0040720468 * sRoot;
        var a = 1.9779984951 * lRoot - 2.4285922050 * mRoot + 0.4505937099 * sRoot;
        var b = 0.0259040371 * lRoot + 0.7827717662 * mRoot - 0.8086757660 * sRoot;

        var hue = Math.Atan2(b, a) * 180 / Math.PI;
        if (hue < 0) hue += 360;
        return new OklchColor(lightness, Math.Sqrt(a * a + b * b), hue);
    }

    /// <summary>
    /// The sRGB colour of an Oklch value when every channel falls inside the gamut. Unlike
    /// <see cref="FromOklch"/> it does not clamp, so callers can tell a saturated colour that a
    /// screen cannot show from one it can.
    /// </summary>
    public static bool TryFromOklch(OklchColor color, out SrgbColor result)
    {
        result = default;
        var (red, green, blue) = ToLinearChannels(color);
        const double tolerance = 1e-6;
        if (red is < -tolerance or > 1 + tolerance
            || green is < -tolerance or > 1 + tolerance
            || blue is < -tolerance or > 1 + tolerance)
            return false;
        result = new SrgbColor(ToByte(red), ToByte(green), ToByte(blue));
        return true;
    }

    /// <summary>
    /// The sRGB colour of an Oklch value with each channel clamped to the gamut. Callers that need
    /// an exact colour ask <see cref="OklchColor.InGamut"/> first.
    /// </summary>
    public static SrgbColor FromOklch(OklchColor color)
    {
        var (red, green, blue) = ToLinearChannels(color.Clamped);
        return new SrgbColor(ToByte(red), ToByte(green), ToByte(blue));
    }

    /// <summary>
    /// Mixes in sRGB the way <c>color-mix(in srgb, …)</c> does, so a generated accent uses the same
    /// recipe as the one in app.css and never drifts from the CSS it replaces.
    /// </summary>
    /// <param name="firstWeight">The share of <paramref name="first"/>, from 0 to 1.</param>
    public static SrgbColor Mix(SrgbColor first, SrgbColor second, double firstWeight)
    {
        var weight = Math.Clamp(firstWeight, 0, 1);
        byte Channel(byte one, byte other) => (byte)Math.Round(one * weight + other * (1 - weight));
        return new SrgbColor(Channel(first.Red, second.Red), Channel(first.Green, second.Green),
            Channel(first.Blue, second.Blue));
    }

    public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"#{Red:x2}{Green:x2}{Blue:x2}");

    public static bool TryParse(string? text, out SrgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var hex = text.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length == 8) hex = hex[..6];
        if (hex.Length != 6) return false;
        if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            || !byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            || !byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            return false;
        color = new SrgbColor(red, green, blue);
        return true;
    }

    public static SrgbColor Parse(string text) =>
        TryParse(text, out var color) ? color : throw new FormatException($"'{text}' is not a #rrggbb colour.");

    public override string ToString() => ToHex();

    private static (double Red, double Green, double Blue) ToLinearChannels(OklchColor color)
    {
        var radians = color.Hue * Math.PI / 180;
        var a = color.Chroma * Math.Cos(radians);
        var b = color.Chroma * Math.Sin(radians);

        var lRoot = color.Lightness + 0.3963377774 * a + 0.2158037573 * b;
        var mRoot = color.Lightness - 0.1055613458 * a - 0.0638541728 * b;
        var sRoot = color.Lightness - 0.0894841775 * a - 1.2914855480 * b;

        var l = lRoot * lRoot * lRoot;
        var m = mRoot * mRoot * mRoot;
        var s = sRoot * sRoot * sRoot;

        return (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
    }

    private static double ToLinear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static byte ToByte(double linear)
    {
        var clamped = Math.Clamp(linear, 0, 1);
        var encoded = clamped <= 0.0031308
            ? 12.92 * clamped
            : 1.055 * Math.Pow(clamped, 1 / 2.4) - 0.055;
        return (byte)Math.Clamp(Math.Round(encoded * 255), 0, 255);
    }
}
