using System.Globalization;
using Lab.Core.Processes;

namespace ImageLab.Core.Imaging;

/// <summary>Pixel dimensions of an image.</summary>
public readonly record struct ImageDimensions(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>
/// Probes image dimensions with ffprobe. VideoLab v1 requires the source image to be exactly
/// 3840x2160 (no hidden scaling), so this is how that gate is enforced.
/// </summary>
public sealed class ImageProbeService
{
    private readonly ProcessRunner _runner;
    private readonly string _ffprobePath;

    public ImageProbeService(ProcessRunner runner, string ffprobePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffprobePath);
        _runner = runner;
        _ffprobePath = ffprobePath;
    }

    /// <summary>Returns the image's dimensions, or null if ffprobe failed or output was unparseable.</summary>
    public async Task<ImageDimensions?> GetDimensionsAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        // csv=s=x:p=0 prints just "WIDTHxHEIGHT", e.g. "3840x2160".
        string[] args =
        [
            "-v", "error",
            "-select_streams", "v:0",
            "-show_entries", "stream=width,height",
            "-of", "csv=s=x:p=0",
            imagePath,
        ];

        var result = await _runner.RunAsync(_ffprobePath, args, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return null;
        }

        return TryParseDimensions(result.StandardOutput, out var dims) ? dims : null;
    }

    /// <summary>Parses ffprobe's <c>WIDTHxHEIGHT</c> CSV output.</summary>
    public static bool TryParseDimensions(string? ffprobeOutput, out ImageDimensions dimensions)
    {
        dimensions = default;
        if (string.IsNullOrWhiteSpace(ffprobeOutput))
        {
            return false;
        }

        var parts = ffprobeOutput.Trim().Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            || w <= 0 || h <= 0)
        {
            return false;
        }

        dimensions = new ImageDimensions(w, h);
        return true;
    }
}
