using Lab.UI.Controls;
namespace VideoLab.Core.Media;

/// <summary>
/// Immutable snapshot of everything one VideoLab render needs. Taken before the render
/// starts so later UI edits cannot mutate an in-flight job.
/// </summary>
/// <param name="AudioPath">Source audio (delivered to the output untouched).</param>
/// <param name="ImagePath">Source still image (must already match <paramref name="Width"/>x<paramref name="Height"/>).</param>
/// <param name="OutputPath">Final MP4 path; the render writes a sibling <c>.partial.mp4</c> first.</param>
/// <param name="BarHex">Bar color, canonical <c>#RRGGBB</c>.</param>
/// <param name="PeakHex">Peak/accent color, canonical <c>#RRGGBB</c> (bar color darkened 30%).</param>
/// <param name="Opacity">Visualizer opacity (fixed 0.35 for v1).</param>
/// <param name="Width">Output width (3840).</param>
/// <param name="Height">Output height (2160).</param>
/// <param name="FrameRate">Output frame rate (60).</param>
/// <param name="BarCount">Visible bar count (40).</param>
public sealed record VideoRenderJob(
    string AudioPath,
    string ImagePath,
    string OutputPath,
    string BarHex,
    string PeakHex,
    double Opacity,
    int Width,
    int Height,
    int FrameRate,
    int BarCount)
{
    // VideoLab v1 product constants — see the plan's "Key Product Decisions".
    public const int DefaultWidth = 3840;
    public const int DefaultHeight = 2160;
    public const int DefaultFrameRate = 60;
    public const int DefaultBarCount = 40;
    public const double DefaultOpacity = 0.35;

    /// <summary>
    /// Builds a job with the suite defaults, normalizing the chosen bar color and deriving
    /// the peak color (bar color darkened by 30%).
    /// </summary>
    public static VideoRenderJob Create(string audioPath, string imagePath, string outputPath, string barColor)
    {
        var bar = ColorMath.Parse(barColor);
        var peak = ColorMath.PeakFor(bar);
        return new VideoRenderJob(
            audioPath,
            imagePath,
            outputPath,
            bar.ToHex(),
            peak.ToHex(),
            DefaultOpacity,
            DefaultWidth,
            DefaultHeight,
            DefaultFrameRate,
            DefaultBarCount);
    }
}
