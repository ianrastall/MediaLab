using Lab.UI.Controls;
using System.Globalization;
using System.Text;

namespace VideoLab.Core.Media;

/// <summary>
/// Builds the FFmpeg <c>-filter_complex</c> graph for VideoLab's 40-bar visualizer.
///
/// The geometry is the result of measured experiments (see project notes):
/// <list type="bullet">
///   <item>render <c>BarCount + 2</c> frequency columns, then <c>crop</c> the middle
///         <c>BarCount</c> at x-offset 1 — this drops the <b>DC bin</b> (left) and the
///         <b>Nyquist edge</b> (right). The DC bin is otherwise pinned near full height and
///         reads as a permanently "stuck" left bar;</item>
///   <item>the analyzer branch carries <b>no AGC/loudness normalizer</b>: a cascaded 30 Hz
///         high-pass plus a <b>fixed</b> +12 dB gain, so nothing integrates loudness over time
///         (that was the cause of the old "builds up halfway through" drift). The high-pass is
///         run as <see cref="HighpassStages"/> stacked 2-pole stages (a 24 dB/oct rolloff at the
///         same 30 Hz corner) — a single 2-pole stage still passed enough 15–30 Hz subsonic
///         energy that, after the log amplitude scale and +12 dB boost, pinned the two leftmost
///         (lowest-frequency) bars near full height on sustained low-end content, reading as
///         "stuck" bars. The steeper knee rejects that sub-30 Hz junk while leaving real bass
///         just above the corner free to move;</item>
///   <item>columns are nearest-neighbor scaled so each bar is an exact integer width
///         (<c>Width / BarCount</c>), then black separators carve clean gaps;</item>
///   <item>black is keyed out and the whole visualizer is composited at fixed 35% opacity.</item>
/// </list>
/// The <b>final delivered audio branch is never touched</b> — only the analyzer copy is.
/// </summary>
public static class SpectrumBarFilterBuilder
{
    public const int HighpassHz = 30;

    /// <summary>
    /// Number of cascaded 2-pole high-pass stages. Each stage is 12 dB/oct; stacking two gives a
    /// 24 dB/oct rolloff at the same <see cref="HighpassHz"/> corner. This firmly rejects the
    /// sub-30 Hz subsonic energy that otherwise pinned the two leftmost bars (see the type remarks).
    /// </summary>
    public const int HighpassStages = 2;

    public const string AnalyzerVolume = "12dB";
    public const int AnalyzerSampleRate = 48000;
    public const int WinSize = 8192;
    public const double Overlap = 0.875;
    public const int Averaging = 4;
    public const int DefaultSeparatorWidth = 16;

    /// <summary>
    /// X positions (in output pixels) of the bar/bar boundaries — one per gap between
    /// adjacent bars (<c>BarCount - 1</c> of them). A separator box is centered on each.
    /// </summary>
    public static IReadOnlyList<int> SeparatorBoundaries(int width, int barCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(barCount);
        if (width % barCount != 0)
        {
            throw new ArgumentException(
                $"Output width {width} must be an exact multiple of bar count {barCount}.", nameof(width));
        }

        var slot = width / barCount;
        return Enumerable.Range(1, barCount - 1).Select(i => i * slot).ToList();
    }

    /// <summary>Builds the complete <c>filter_complex</c> graph string for <paramref name="job"/>.</summary>
    /// <param name="separatorWidth">Width in px of the black gap between bars.</param>
    public static string Build(VideoRenderJob job, int separatorWidth = DefaultSeparatorWidth)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentOutOfRangeException.ThrowIfNegative(separatorWidth);

        var barColor = ColorMath.Parse(job.BarHex).ToFfmpegHex();   // 0xRRGGBB
        var peakColor = ColorMath.Parse(job.PeakHex).ToFfmpegHex();
        var analyzerColumns = job.BarCount + 2;                      // +DC (left) +Nyquist (right), both cropped off
        var c = CultureInfo.InvariantCulture;

        // Separators carve transparent gaps between bars at full resolution. They use
        // drawbox replace=1 with a fully-transparent color so they set alpha directly — this
        // avoids needing a second 4K colorkey pass after the (already-applied) opacity.
        var separatorFilters = SeparatorBoundaries(job.Width, job.BarCount)
            .Select(boundary =>
            {
                var x = boundary - separatorWidth / 2;
                return $"drawbox=x={x.ToString(c)}:y=0:w={separatorWidth.ToString(c)}:h=ih:color=0x00000000:replace=1:t=fill";
            });

        // Visualizer chain. Performance: keying AND the per-pixel opacity multiply both run on
        // the NARROW pre-scale image (BarCount px wide); only the cheap upscale + drawboxes run
        // at full 4K. ~30% faster than doing key/alpha after the upscale, with identical output.
        // Order is critical: colorkey OVERWRITES the alpha channel, so the 35% opacity
        // (colorchannelmixer) MUST come after it, or bars render fully opaque.
        var vizFilters = new List<string>
        {
            $"showfreqs=s={analyzerColumns}x{job.Height}:r={job.FrameRate}:mode=bar:ascale=log:fscale=log:" +
            $"colors={barColor}|{peakColor}:win_size={WinSize}:overlap={Overlap.ToString(c)}:averaging={Averaging}",
            $"crop={job.BarCount}:{job.Height}:1:0",          // drop DC (left) + Nyquist (right)
            "format=rgba",
            "colorkey=0x000000:0.08:0.1",                     // black background -> transparent (overwrites alpha)
            $"colorchannelmixer=aa={job.Opacity.ToString(c)}", // THEN 35% opacity on the bars (survives)
            $"scale={job.Width}:{job.Height}:flags=neighbor",
        };
        vizFilters.AddRange(separatorFilters);

        var sb = new StringBuilder();

        // Final audio branch (untouched) + analyzer copy.
        sb.Append("[1:a]asplit=2[aout][avisrc];");

        // Analyzer branch: stereo @ 48k, cascaded high-pass to kill DC/sub-sonic, fixed boost.
        // No loudness normalizer, so it never drifts over time.
        var highpass = string.Join(",", Enumerable.Repeat($"highpass=f={HighpassHz.ToString(c)}:poles=2", HighpassStages));
        sb.Append(c, $"[avisrc]aformat=channel_layouts=stereo,aresample={AnalyzerSampleRate},{highpass},volume={AnalyzerVolume}[avis];");

        // Background still (overlay handles format mixing; no full-frame rgba pass needed).
        sb.Append("[0:v]setsar=1[bg];");

        // Visualizer.
        sb.Append("[avis]").Append(string.Join(",", vizFilters)).Append("[viz];");

        // Composite + final pixel format.
        sb.Append(c, $"[bg][viz]overlay=0:0:format=auto,fps={job.FrameRate},format=yuv420p[v]");

        return sb.ToString();
    }
}
