using LyricsLab.Core.Lyrics;
using Lab.Core.Tools;

namespace MediaLab.Tests.TestSupport;

/// <summary>
/// Locates the real bundled CLI tools for integration tests. Resolution uses the same
/// <see cref="ToolLocator"/> the apps use (PATH + the D:\cli\tools defaults), so these
/// tests exercise genuine tool interaction rather than mocks. When a tool is absent the
/// integration test self-skips by returning early.
/// </summary>
internal static class RealTools
{
    private static readonly ToolLocator Locator = new();

    public static string? Ffmpeg => Locator.Locate("ffmpeg");

    public static string? Ffprobe => Locator.Locate("ffprobe");

    public static string? WhisperCli => Locator.Locate("whisper-cli");

    public static string? WhisperModel => new WhisperTranscriberService(Locator).FindModel();

    public static readonly string ComSpec =
        Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
}
