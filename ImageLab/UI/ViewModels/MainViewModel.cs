using CommunityToolkit.Mvvm.ComponentModel;
using ImageLab.Core.Imaging;
using Lab.Core.Processes;
using Lab.Core.Tools;
using Lab.UI.Services;
using Lab.UI.ViewModels;

namespace ImageLab.UI.ViewModels;

/// <summary>Root view model for ImageLab: tool status, log, and the single-image editor.</summary>
public sealed class MainViewModel : ObservableObject
{
    private static readonly string[] RequiredTools = ["magick"];

    public MainViewModel()
    {
        var locator = new ToolLocator();
        Tools = new ToolStatusViewModel(locator, RequiredTools);
        Terminal = new TerminalViewModel();

        var magick = Tools.PathOf("magick");
        var ops = magick is null ? null : new ImageOpsService(new ProcessRunner(), magick);
        Edit = new ImageEditViewModel(ops, new FilePickerService(), Terminal, Tools.AllPresent);
    }

    public ToolStatusViewModel Tools { get; }

    public TerminalViewModel Terminal { get; }

    public ImageEditViewModel Edit { get; }
}
