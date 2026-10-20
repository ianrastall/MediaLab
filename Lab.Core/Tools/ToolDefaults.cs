namespace Lab.Core.Tools;

/// <summary>
/// Friendly fallback locations for the suite's bundled CLI tools. The portable root is a
/// <c>tools</c> folder beside the executable, so a shipped build is self-contained. A legacy
/// developer root is searched after it so the author's centralized layout keeps working without
/// configuration. These are the lowest-priority source in <see cref="ToolLocator"/> — bundled and
/// configured paths always win.
/// </summary>
public static class ToolDefaults
{
    /// <summary>
    /// Portable default root: a <c>tools</c> folder beside the executable. Shipped/bundled builds
    /// place the CLI tools (ffmpeg, whisper-cli, …) here so the app needs no external configuration.
    /// </summary>
    public static readonly string PortableRoot = Path.Combine(AppContext.BaseDirectory, "tools");

    /// <summary>
    /// Legacy developer fallback — the original centralized CLI folder on the author's machine.
    /// Searched last and harmless on end-user machines (the path simply does not exist there).
    /// </summary>
    public const string LegacyDevRoot = @"D:\cli\tools";

    /// <summary>Default roots searched in priority order: portable beside-exe first, then the legacy dev folder.</summary>
    public static readonly IReadOnlyList<string> DefaultRoots = [PortableRoot, LegacyDevRoot];

    /// <summary>Primary (portable) default root. Back-compat alias for callers expecting a single root.</summary>
    public static string DefaultRoot => PortableRoot;

    // tool name (lower-case, no extension) -> path relative to the root.
    private static readonly IReadOnlyDictionary<string, string> RelativePaths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ffmpeg"] = @"ffmpeg\ffmpeg.exe",
            ["ffprobe"] = @"ffmpeg\ffprobe.exe",
            ["sox"] = @"sox\sox.exe",
            ["rubberband"] = @"rubberband\rubberband.exe",
            ["rubberband-r3"] = @"rubberband\rubberband-r3.exe",
            ["rsgain"] = @"rsgain\rsgain.exe",
            ["flac"] = @"flac\flac.exe",
            ["metaflac"] = @"flac\metaflac.exe",
            ["mediainfo"] = @"MediaInfo\MediaInfo.exe",
            ["magick"] = @"ImageMagick\magick.exe",
            ["whisper-cli"] = @"whisper\whisper-cli.exe",
            ["whisper"] = @"whisper\whisper-cli.exe",
        };

    /// <summary>
    /// Builds the absolute default path for <paramref name="toolName"/> under <paramref name="root"/>,
    /// or null if the tool has no known default location.
    /// </summary>
    public static string? For(string toolName, string? root = null)
    {
        if (!RelativePaths.TryGetValue(toolName, out var relative))
        {
            return null;
        }

        return Path.Combine(root ?? DefaultRoot, relative);
    }
}
