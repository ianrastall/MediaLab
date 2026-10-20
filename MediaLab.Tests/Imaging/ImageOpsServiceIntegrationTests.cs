using ImageLab.Core.Imaging;
using Lab.Core.Processes;
using Lab.Core.Tools;
using MediaLab.Tests.TestSupport;

namespace MediaLab.Tests.Imaging;

/// <summary>Drives the real ImageMagick binary. Self-skips when it's not installed.</summary>
[Trait("Category", "Integration")]
public class ImageOpsServiceIntegrationTests
{
    private static (ImageOpsService svc, ProcessRunner runner, string magick)? Build()
    {
        var magick = new ToolLocator().Locate("magick");
        if (magick is null)
        {
            return null;
        }

        var runner = new ProcessRunner();
        return (new ImageOpsService(runner, magick), runner, magick);
    }

    [Fact]
    public async Task ExportAsync_AppliesEditsAndFitsTo4K()
    {
        if (Build() is not { } b)
        {
            return;
        }

        using var temp = new TempDir();
        var src = temp.Combine("src.png");
        var outPng = temp.Combine("out.png");

        // A non-4K, non-16:9 source so Fill must crop/scale to exactly 3840x2160.
        var gen = await b.runner.RunAsync(b.magick, ["-size", "1000x1500", "xc:navy", src]);
        Assert.True(gen.Success, gen.StandardError);

        var adjustments = new ImageAdjustments
        {
            Brightness = 5,
            Contrast = 10,
            Saturation = 120,
            Chroma = 95,
            Red = 10,
            Blue = -10,
            ResizeMode = ResizeMode.Fill,
        };

        var ok = await b.svc.ExportAsync(src, adjustments, outPng);
        Assert.True(ok, "export failed");

        var dims = await b.svc.GetDimensionsAsync(outPng);
        Assert.Equal(new ImageDimensions(3840, 2160), dims);
    }

    [Fact]
    public async Task RenderPreviewAsync_ProducesDownscaledImage()
    {
        if (Build() is not { } b)
        {
            return;
        }

        using var temp = new TempDir();
        var src = temp.Combine("src.png");
        var preview = temp.Combine("preview.png");

        var gen = await b.runner.RunAsync(b.magick, ["-size", "3840x2160", "xc:teal", src]);
        Assert.True(gen.Success, gen.StandardError);

        var result = await b.svc.RenderPreviewAsync(src, new ImageAdjustments(), preview, maxWidth: 640);

        Assert.Equal(preview, result);
        var dims = await b.svc.GetDimensionsAsync(preview);
        Assert.NotNull(dims);
        Assert.True(dims!.Value.Width <= 640, $"preview width {dims.Value.Width} should be <= 640");
    }
}
