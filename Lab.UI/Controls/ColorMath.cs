using System.Globalization;

namespace Lab.UI.Controls;

/// <summary>An 8-bit-per-channel RGB color.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Canonical <c>#RRGGBB</c> (upper-case) form used throughout the suite.</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>FFmpeg color literal form, <c>0xRRGGBB</c>.</summary>
    public string ToFfmpegHex() => $"0x{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// Color parsing and the suite's deterministic color transforms. Pure and culture-independent
/// so the render pipeline produces identical FFmpeg color literals on any machine.
/// </summary>
public static class ColorMath
{
    /// <summary>The peak/accent color is the selected bar color darkened by this fraction.</summary>
    public const double PeakDarkenFraction = 0.30;

    /// <summary>
    /// Parses <c>#RGB</c>, <c>#RRGGBB</c> (with or without the leading <c>#</c>) into an
    /// <see cref="RgbColor"/>. Whitespace is trimmed; parsing is case-insensitive.
    /// </summary>
    public static bool TryParse(string? input, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        switch (text.Length)
        {
            case 3:
                // #RGB -> each nibble doubled (#RGB == #RRGGBB).
                if (TryNibble(text[0], out var r3) && TryNibble(text[1], out var g3) && TryNibble(text[2], out var b3))
                {
                    color = new RgbColor((byte)(r3 * 17), (byte)(g3 * 17), (byte)(b3 * 17));
                    return true;
                }

                return false;

            case 6:
                if (TryByte(text.AsSpan(0, 2), out var r6)
                    && TryByte(text.AsSpan(2, 2), out var g6)
                    && TryByte(text.AsSpan(4, 2), out var b6))
                {
                    color = new RgbColor(r6, g6, b6);
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>Parses <paramref name="input"/> or throws <see cref="FormatException"/>.</summary>
    public static RgbColor Parse(string input) =>
        TryParse(input, out var color) ? color : throw new FormatException($"Invalid color: '{input}'.");

    /// <summary>Normalizes any accepted input to canonical <c>#RRGGBB</c> upper-case form.</summary>
    public static string Normalize(string input) => Parse(input).ToHex();

    /// <summary>
    /// Darkens a color by multiplying each channel by <c>(1 - fraction)</c>, rounding
    /// half away from zero and clamping to [0, 255].
    /// </summary>
    /// <param name="fraction">Amount to darken in [0, 1]; e.g. 0.30 makes the channel 70% as bright.</param>
    public static RgbColor Darken(RgbColor color, double fraction)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fraction, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fraction, 1.0);

        var factor = 1.0 - fraction;
        return new RgbColor(Scale(color.R, factor), Scale(color.G, factor), Scale(color.B, factor));
    }

    /// <summary>The peak/accent color for a given bar color (darkened by <see cref="PeakDarkenFraction"/>).</summary>
    public static RgbColor PeakFor(RgbColor barColor) => Darken(barColor, PeakDarkenFraction);

    private static byte Scale(byte channel, double factor)
    {
        var scaled = Math.Round(channel * factor, MidpointRounding.AwayFromZero);
        return (byte)Math.Clamp(scaled, 0, 255);
    }

    private static bool TryByte(ReadOnlySpan<char> hex, out byte value) =>
        byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    private static bool TryNibble(char c, out int value)
    {
        if (byte.TryParse([c], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            value = b;
            return true;
        }

        value = 0;
        return false;
    }
}
