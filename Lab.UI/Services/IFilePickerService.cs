namespace Lab.UI.Services;

/// <summary>Abstraction over the platform open/save dialogs so view models stay testable.</summary>
public interface IFilePickerService
{
    /// <summary>Shows an open-file dialog; returns the chosen path or null if cancelled.</summary>
    string? PickOpenFile(string title, string filter, string? initialDirectory = null);

    /// <summary>Shows a save-file dialog; returns the chosen path or null if cancelled.</summary>
    string? PickSaveFile(string title, string filter, string? defaultFileName = null, string? initialDirectory = null);

    /// <summary>Opens the OS file browser with <paramref name="path"/> selected, if it exists.</summary>
    void RevealInExplorer(string path);
}
