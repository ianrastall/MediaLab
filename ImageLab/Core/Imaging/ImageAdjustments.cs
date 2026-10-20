namespace ImageLab.Core.Imaging;

/// <summary>A crop rectangle in source pixels.</summary>
public readonly record struct CropRect(int X, int Y, int Width, int Height);

/// <summary>How the image is fit to the target canvas.</summary>
public enum ResizeMode
{
    /// <summary>No resize — keep the (possibly cropped) source size.</summary>
    None,

    /// <summary>Scale to fit inside the canvas, preserving aspect (may letterbox visually).</summary>
    Fit,

    /// <summary>Scale to cover the canvas, preserving aspect, then center-crop to exact size.</summary>
    Fill,

    /// <summary>Stretch to the exact canvas size, ignoring aspect.</summary>
    Stretch,
}

/// <summary>
/// A set of non-destructive image edits ImageLab applies to a single image. Neutral values mean
/// "no change", so an all-default instance is a passthrough. Maps to an ImageMagick pipeline via
/// <see cref="ImageMagickArgsBuilder"/>.
/// </summary>
public sealed record ImageAdjustments
{
    /// <summary>Optional crop applied first.</summary>
    public CropRect? Crop { get; init; }

    /// <summary>Brightness, -100..100 (0 = neutral).</summary>
    public int Brightness { get; init; }

    /// <summary>Contrast, -100..100 (0 = neutral).</summary>
    public int Contrast { get; init; }

    /// <summary>Gamma, 0.1..3.0 (1.0 = neutral; &gt;1 brightens midtones, &lt;1 darkens).</summary>
    public double Gamma { get; init; } = 1.0;

    /// <summary>Saturation, 0..200 (100 = neutral, 0 = grayscale).</summary>
    public int Saturation { get; init; } = 100;

    /// <summary>Chroma / hue rotation, 0..200 (100 = neutral; 0 and 200 = ±180°).</summary>
    public int Chroma { get; init; } = 100;

    /// <summary>Red channel, -100..100 (0 = neutral; +100 doubles, -100 zeroes).</summary>
    public int Red { get; init; }

    /// <summary>Green channel, -100..100 (0 = neutral).</summary>
    public int Green { get; init; }

    /// <summary>Blue channel, -100..100 (0 = neutral).</summary>
    public int Blue { get; init; }

    /// <summary>How to fit the result to the target canvas.</summary>
    public ResizeMode ResizeMode { get; init; } = ResizeMode.Fill;

    /// <summary>Target canvas width (default 4K for VideoLab handoff).</summary>
    public int TargetWidth { get; init; } = 3840;

    /// <summary>Target canvas height.</summary>
    public int TargetHeight { get; init; } = 2160;
}
