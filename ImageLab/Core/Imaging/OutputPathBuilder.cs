using System.IO;
namespace ImageLab.Core.Imaging;

/// <summary>
/// Builds VideoLab's output and staging paths. The render writes to a <c>.partial.mp4</c>
/// sibling and is promoted to the final name only on success.
/// </summary>
public static class OutputPathBuilder
{
    /// <summary>
    /// Default output for a given audio file: the audio stem plus <c>_video.mp4</c>,
    /// placed next to the audio file.
    /// </summary>
    public static string ForAudio(string audioPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(audioPath)) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(audioPath);
        return Path.Combine(directory, $"{stem}_video.mp4");
    }

    /// <summary>
    /// The staging path for a final output: <c>name.partial.mp4</c> beside <c>name.mp4</c>.
    /// </summary>
    public static string Partial(string finalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(finalPath)) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(finalPath);
        var ext = Path.GetExtension(finalPath); // includes the leading dot, e.g. ".mp4"
        return Path.Combine(directory, $"{stem}.partial{ext}");
    }
}
