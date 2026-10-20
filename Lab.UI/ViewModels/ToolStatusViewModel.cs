using CommunityToolkit.Mvvm.ComponentModel;
using Lab.Core.Tools;

namespace Lab.UI.ViewModels;

/// <summary>
/// Shared across the suite: resolves an app's required CLI tools and reports a one-line status.
/// Each app passes its own required set (VideoLab: ffmpeg/ffprobe; AudioLab: the mastering chain;
/// LyricsLab: whisper + ffmpeg/ffprobe).
/// </summary>
public sealed partial class ToolStatusViewModel : ObservableObject
{
    private readonly ToolLocator _locator;
    private readonly IReadOnlyList<string> _requiredTools;

    public ToolStatusViewModel(ToolLocator locator, IReadOnlyList<string> requiredTools)
    {
        _locator = locator;
        _requiredTools = requiredTools;
        Refresh();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void Refresh()
    {
        var verifier = new DependencyVerifier(_locator);
        Tools = verifier.Verify(_requiredTools);
        OnPropertyChanged(nameof(Tools));

        AllPresent = Tools.All(s => s.Found);

        StatusText = AllPresent
            ? $"{Tools.Count} tool(s) ready"
            : "Missing: " + string.Join(", ", Tools.Where(s => !s.Found).Select(s => s.Tool));
    }

    /// <summary>Per-tool resolution result, in the order requested.</summary>
    public IReadOnlyList<DependencyStatus> Tools { get; private set; } = [];

    [ObservableProperty]
    private bool allPresent;

    [ObservableProperty]
    private string statusText = string.Empty;

    /// <summary>Resolved absolute path for a tool, or null if it wasn't found.</summary>
    public string? PathOf(string tool) =>
        Tools.FirstOrDefault(s => string.Equals(s.Tool, tool, StringComparison.OrdinalIgnoreCase))?.Path;
}
