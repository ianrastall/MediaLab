using System.IO;
using System.Globalization;
using ImageLab.Core.Imaging;
using Lab.Core.Processes;

namespace ImageLab.Core.Imaging;

/// <summary>
/// Runs ImageMagick (<c>magick</c>) to apply <see cref="ImageAdjustments"/>: full-resolution export
/// and fast downscaled previews. The same edit operators drive both, so the preview matches the
/// export exactly (only the preview adds a final downscale).
/// </summary>
public sealed class ImageOpsService
{
    private readonly ProcessRunner _runner;
    private readonly string _magickPath;

    public ImageOpsService(ProcessRunner runner, string magickPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magickPath);
        _runner = runner;
        _magickPath = magickPath;
    }

    /// <summary>Reads the pixel dimensions of an image via <c>magick identify</c>.</summary>
    public async Task<ImageDimensions?> GetDimensionsAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        var result = await _runner.RunAsync(
            _magickPath,
            ["identify", "-ping", "-format", "%wx%h", imagePath],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.Success && ImageProbeService.TryParseDimensions(result.StandardOutput, out var dims) ? dims : null;
    }

    /// <summary>Applies the adjustments at full resolution and writes <paramref name="outputPath"/>.</summary>
    public async Task<bool> ExportAsync(
        string inputPath,
        ImageAdjustments adjustments,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        // Write to a .partial file first; only promote to the final path on success so a failed
        // or cancelled export never leaves a corrupt file at the destination.
        var partialPath = ImageLab.Core.Imaging.OutputPathBuilder.Partial(outputPath);
        try
        {
            var args = new List<string> { inputPath };
            args.AddRange(ImageMagickArgsBuilder.BuildEditArgs(adjustments));
            args.Add(partialPath);

            var result = await _runner.RunAsync(_magickPath, args, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success)
                return false;

            File.Move(partialPath, outputPath, overwrite: true);
            return true;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
    }

    /// <summary>
    /// Renders a downscaled preview (same edits, then fit to <paramref name="maxWidth"/>) for fast,
    /// exact-look UI feedback. Returns the preview path on success, or null on failure.
    /// </summary>
    public async Task<string?> RenderPreviewAsync(
        string inputPath,
        ImageAdjustments adjustments,
        string previewPath,
        int maxWidth = 960,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { inputPath };
        args.AddRange(ImageMagickArgsBuilder.BuildEditArgs(adjustments));
        args.Add("-resize");
        args.Add($"{maxWidth.ToString(CultureInfo.InvariantCulture)}x>"); // only shrink, never enlarge
        args.Add(previewPath);

        var result = await _runner.RunAsync(_magickPath, args, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Success ? previewPath : null;
    }
}
