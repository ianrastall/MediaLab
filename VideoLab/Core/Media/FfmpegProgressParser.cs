using System.Globalization;

namespace VideoLab.Core.Media;

/// <summary>One completed ffmpeg progress block.</summary>
/// <param name="OutTime">Encoded position, or null if ffmpeg reported it as <c>N/A</c>.</param>
/// <param name="IsEnd">True for the final block (<c>progress=end</c>).</param>
public readonly record struct FfmpegProgress(TimeSpan? OutTime, bool IsEnd)
{
    /// <summary>
    /// Percent complete given a known total duration, clamped to [0, 100]. Returns 0 when the
    /// position or total is unavailable.
    /// </summary>
    public double PercentOf(TimeSpan total)
    {
        if (OutTime is not { } t || total <= TimeSpan.Zero)
        {
            return 0;
        }

        return Math.Clamp(t / total * 100.0, 0, 100);
    }
}

/// <summary>
/// Parses ffmpeg <c>-progress</c> output. ffmpeg emits blocks of <c>key=value</c> lines, each
/// terminated by a <c>progress=continue</c> (or <c>progress=end</c>) line — so this accumulates
/// keys until a terminator, then yields one <see cref="FfmpegProgress"/>.
///
/// Important quirk verified on ffmpeg 8.1: the <c>out_time_ms</c> field is actually
/// MICROSECONDS, not milliseconds (it equals <c>out_time_us</c>). We therefore read
/// <c>out_time_us</c> and divide by 1,000,000; early blocks report it as <c>N/A</c>.
/// </summary>
public sealed class FfmpegProgressParser
{
    private readonly Dictionary<string, string> _block = new(StringComparer.Ordinal);

    /// <summary>
    /// Feeds one line. Returns true and sets <paramref name="progress"/> when that line
    /// completed a block; otherwise accumulates and returns false.
    /// </summary>
    public bool TryConsume(string line, out FfmpegProgress progress)
    {
        progress = default;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var eq = line.IndexOf('=');
        if (eq <= 0)
        {
            return false;
        }

        var key = line[..eq].Trim();
        var value = line[(eq + 1)..].Trim();

        if (!key.Equals("progress", StringComparison.Ordinal))
        {
            _block[key] = value;
            return false;
        }

        progress = new FfmpegProgress(ExtractOutTime(), value.Equals("end", StringComparison.Ordinal));
        _block.Clear();
        return true;
    }

    private TimeSpan? ExtractOutTime()
    {
        if (_block.TryGetValue("out_time_us", out var us)
            && long.TryParse(us, NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros)
            && micros >= 0)
        {
            return TimeSpan.FromMicroseconds(micros);
        }

        return null;
    }
}
