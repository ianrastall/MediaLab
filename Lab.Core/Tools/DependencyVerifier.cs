namespace Lab.Core.Tools;

/// <summary>Resolution status of one required tool.</summary>
/// <param name="Tool">Tool name as requested (no extension).</param>
/// <param name="Found">True when an executable was located.</param>
/// <param name="Path">Absolute path when found; otherwise null.</param>
public sealed record DependencyStatus(string Tool, bool Found, string? Path);

/// <summary>
/// Checks that the tools an app needs can be located. Apps declare their own
/// required set (VideoLab: ffmpeg + ffprobe; AudioLab: the full mastering chain).
/// </summary>
public sealed class DependencyVerifier
{
    private readonly ToolLocator _locator;

    public DependencyVerifier(ToolLocator locator)
    {
        _locator = locator;
    }

    /// <summary>Resolves every tool in <paramref name="requiredTools"/>, preserving order.</summary>
    public IReadOnlyList<DependencyStatus> Verify(IEnumerable<string> requiredTools)
    {
        var results = new List<DependencyStatus>();
        foreach (var tool in requiredTools)
        {
            var path = _locator.Locate(tool);
            results.Add(new DependencyStatus(tool, path is not null, path));
        }

        return results;
    }

    /// <summary>True when every required tool resolved.</summary>
    public bool AllPresent(IEnumerable<string> requiredTools) => Verify(requiredTools).All(s => s.Found);
}
