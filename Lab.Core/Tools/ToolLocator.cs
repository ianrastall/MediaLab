namespace Lab.Core.Tools;

/// <summary>
/// Resolves a CLI tool to an absolute executable path using a fixed precedence:
/// <list type="number">
///   <item><b>Bundled</b>: <c>&lt;baseDir&gt;\bin\&lt;tool&gt;.exe</c> shipped alongside the app;</item>
///   <item><b>Configured</b>: an explicit path supplied by the user/settings;</item>
///   <item><b>PATH</b>: the first match on the environment <c>PATH</c>;</item>
///   <item><b>Defaults</b>: the friendly fallback roots — portable <c>&lt;app&gt;\tools</c> then the legacy dev folder (<see cref="ToolDefaults"/>).</item>
/// </list>
/// Bundled wins so a shipped build is reproducible; defaults are last so they only help
/// unconfigured dev machines. All inputs are injectable to keep the type unit-testable.
/// </summary>
public sealed class ToolLocator
{
    private readonly string _baseDirectory;
    private readonly IReadOnlyDictionary<string, string> _configuredPaths;
    private readonly IReadOnlyList<string> _pathDirectories;
    private readonly IReadOnlyList<string> _defaultsRoots;

    public ToolLocator(
        string? baseDirectory = null,
        IReadOnlyDictionary<string, string>? configuredPaths = null,
        IEnumerable<string>? pathDirectories = null,
        string? defaultsRoot = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _configuredPaths = configuredPaths ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _pathDirectories = (pathDirectories ?? GetEnvironmentPathDirectories()).ToList();
        // An explicit root overrides the defaults (used by tests); otherwise search portable → legacy.
        _defaultsRoots = defaultsRoot is not null ? [defaultsRoot] : ToolDefaults.DefaultRoots;
    }

    /// <summary>
    /// Returns the absolute path to <paramref name="toolName"/> (no extension, e.g. "ffmpeg"),
    /// or null if it cannot be found in any source.
    /// </summary>
    public string? Locate(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var exeName = toolName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? toolName
            : toolName + ".exe";

        // 1. Bundled bin/.
        var bundled = Path.Combine(_baseDirectory, "bin", exeName);
        if (File.Exists(bundled))
        {
            return bundled;
        }

        // 2. Configured explicit path.
        if (_configuredPaths.TryGetValue(toolName, out var configured)
            && !string.IsNullOrWhiteSpace(configured)
            && File.Exists(configured))
        {
            return configured;
        }

        // 3. PATH directories.
        foreach (var dir in _pathDirectories)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            var candidate = Path.Combine(dir, exeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 4. Friendly default roots: portable <app>\tools first, then the legacy dev folder.
        foreach (var root in _defaultsRoots)
        {
            var fallback = ToolDefaults.For(toolName, root);
            if (fallback is not null && File.Exists(fallback))
            {
                return fallback;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetEnvironmentPathDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        return string.IsNullOrEmpty(path)
            ? []
            : path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
