using Lab.Core.Tools;
using MediaLab.Tests.TestSupport;

namespace MediaLab.Tests.Tools;

public class ToolLocatorTests
{
    [Fact]
    public void Locate_PrefersBundledBin()
    {
        using var temp = new TempDir();
        var bundled = temp.TouchFile(@"bin\ffmpeg.exe");
        var configured = temp.TouchFile(@"configured\ffmpeg.exe");

        var locator = new ToolLocator(
            baseDirectory: temp.Path,
            configuredPaths: new Dictionary<string, string> { ["ffmpeg"] = configured });

        Assert.Equal(bundled, locator.Locate("ffmpeg"));
    }

    [Fact]
    public void Locate_UsesConfiguredPath_WhenNoBundled()
    {
        using var temp = new TempDir();
        var configured = temp.TouchFile(@"configured\ffmpeg.exe");

        var locator = new ToolLocator(
            baseDirectory: temp.Combine("emptybase"),
            configuredPaths: new Dictionary<string, string> { ["ffmpeg"] = configured });

        Assert.Equal(configured, locator.Locate("ffmpeg"));
    }

    [Fact]
    public void Locate_FallsBackToPathDirectories()
    {
        using var temp = new TempDir();
        var onPath = temp.TouchFile(@"pathdir\ffmpeg.exe");

        var locator = new ToolLocator(
            baseDirectory: temp.Combine("emptybase"),
            pathDirectories: [temp.Combine("pathdir")]);

        Assert.Equal(onPath, locator.Locate("ffmpeg"));
    }

    [Fact]
    public void Locate_FallsBackToFriendlyDefaults_Last()
    {
        using var temp = new TempDir();
        // Lay out a fake "D:\cli\tools" style root: <root>\ffmpeg\ffmpeg.exe
        var def = temp.TouchFile(@"defaults\ffmpeg\ffmpeg.exe");

        var locator = new ToolLocator(
            baseDirectory: temp.Combine("emptybase"),
            pathDirectories: [],
            defaultsRoot: temp.Combine("defaults"));

        Assert.Equal(def, locator.Locate("ffmpeg"));
    }

    [Fact]
    public void Locate_ReturnsNull_WhenNowhere()
    {
        using var temp = new TempDir();
        var locator = new ToolLocator(
            baseDirectory: temp.Combine("emptybase"),
            pathDirectories: [],
            defaultsRoot: temp.Combine("no-defaults-here"));

        Assert.Null(locator.Locate("ffmpeg"));
    }

    [Fact]
    public void Locate_AcceptsNameWithExeExtension()
    {
        using var temp = new TempDir();
        var bundled = temp.TouchFile(@"bin\sox.exe");
        var locator = new ToolLocator(baseDirectory: temp.Path, pathDirectories: []);

        Assert.Equal(bundled, locator.Locate("sox.exe"));
    }

    [Fact]
    public void DependencyVerifier_ReportsPerToolStatus()
    {
        using var temp = new TempDir();
        temp.TouchFile(@"bin\ffmpeg.exe");
        var locator = new ToolLocator(baseDirectory: temp.Path, pathDirectories: [], defaultsRoot: temp.Combine("none"));
        var verifier = new DependencyVerifier(locator);

        var statuses = verifier.Verify(["ffmpeg", "ffprobe"]);

        Assert.Collection(statuses,
            s => { Assert.Equal("ffmpeg", s.Tool); Assert.True(s.Found); Assert.NotNull(s.Path); },
            s => { Assert.Equal("ffprobe", s.Tool); Assert.False(s.Found); Assert.Null(s.Path); });
        Assert.False(verifier.AllPresent(["ffmpeg", "ffprobe"]));
    }
}
