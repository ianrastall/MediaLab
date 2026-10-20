using CommunityToolkit.Mvvm.ComponentModel;
using Lab.Core.Tools;
using Lab.UI.Services;
using Lab.UI.ViewModels;

namespace SongLab.UI.ViewModels;

/// <summary>Root view model for SongLab: composes tool status, log, and the mastering surface.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    // SongLab owns the suite's audio dependency chain.
    private static readonly string[] RequiredTools =
        ["ffmpeg", "sox", "rubberband-r3", "rsgain"];

    public MainViewModel()
    {
        var locator = new ToolLocator();
        Tools = new ToolStatusViewModel(locator, RequiredTools);
        Terminal = new TerminalViewModel();
        
        var runner = new Lab.Core.Processes.ProcessRunner();
        var pipeline = new SongLab.Core.Media.MasteringPipeline(locator, runner);
        
        Mastering = new MasteringViewModel(new FilePickerService(), Terminal, Tools, pipeline);
    }

    public ToolStatusViewModel Tools { get; }

    public TerminalViewModel Terminal { get; }

    public MasteringViewModel Mastering { get; }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenSetup()
    {
        var window = new SongLab.UI.Views.SetupWindow();
        window.ShowDialog();
        Tools.RefreshCommand.Execute(null);
    }
}
