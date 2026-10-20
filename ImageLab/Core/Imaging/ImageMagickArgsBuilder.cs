using System.Globalization;

namespace ImageLab.Core.Imaging;

/// <summary>
/// Builds the ImageMagick operator sequence for a set of <see cref="ImageAdjustments"/>. Returns
/// only the middle operators (no input/output); <see cref="ImageOpsService"/> wraps them with the
/// input file and output target. Pure and culture-independent so commands are identical anywhere.
///
/// <para>Operator order is deliberate: crop → brightness/contrast → saturation/chroma → per-channel
/// RGB → resize/canvas. Only non-neutral adjustments emit operators, so a default instance yields
/// just the resize (or nothing when <see cref="ResizeMode.None"/>).</para>
/// </summary>
public static class ImageMagickArgsBuilder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static IReadOnlyList<string> BuildEditArgs(ImageAdjustments adj)
    {
        ArgumentNullException.ThrowIfNull(adj);
        var args = new List<string>();

        if (adj.Crop is { } crop)
        {
            args.Add("-crop");
            args.Add($"{crop.Width.ToString(Inv)}x{crop.Height.ToString(Inv)}+{crop.X.ToString(Inv)}+{crop.Y.ToString(Inv)}");
            args.Add("+repage");
        }

        if (adj.Brightness != 0 || adj.Contrast != 0)
        {
            args.Add("-brightness-contrast");
            args.Add($"{adj.Brightness.ToString(Inv)}x{adj.Contrast.ToString(Inv)}");
        }

        if (Math.Abs(adj.Gamma - 1.0) > 1e-6)
        {
            args.Add("-gamma");
            args.Add(adj.Gamma.ToString("0.###", Inv));
        }

        if (adj.Saturation != 100 || adj.Chroma != 100)
        {
            // -modulate brightness,saturation,hue — brightness left at 100 (handled separately).
            args.Add("-modulate");
            args.Add($"100,{adj.Saturation.ToString(Inv)},{adj.Chroma.ToString(Inv)}");
        }

        AddChannel(args, "R", adj.Red);
        AddChannel(args, "G", adj.Green);
        AddChannel(args, "B", adj.Blue);
        if (adj.Red != 0 || adj.Green != 0 || adj.Blue != 0)
        {
            args.Add("+channel");
        }

        switch (adj.ResizeMode)
        {
            case ResizeMode.Fit:
                args.Add("-resize");
                args.Add(Canvas(adj));
                break;
            case ResizeMode.Fill:
                args.Add("-resize");
                args.Add(Canvas(adj) + "^");
                args.Add("-gravity");
                args.Add("center");
                args.Add("-extent");
                args.Add(Canvas(adj));
                break;
            case ResizeMode.Stretch:
                args.Add("-resize");
                args.Add(Canvas(adj) + "!");
                break;
            case ResizeMode.None:
            default:
                break;
        }

        return args;
    }

    private static void AddChannel(List<string> args, string channel, int percent)
    {
        if (percent == 0)
        {
            return;
        }

        var factor = 1.0 + (percent / 100.0); // -100 -> 0.0, 0 -> 1.0, +100 -> 2.0
        args.Add("-channel");
        args.Add(channel);
        args.Add("-evaluate");
        args.Add("multiply");
        args.Add(factor.ToString("0.###", Inv));
    }

    private static string Canvas(ImageAdjustments adj) =>
        $"{adj.TargetWidth.ToString(Inv)}x{adj.TargetHeight.ToString(Inv)}";
}
