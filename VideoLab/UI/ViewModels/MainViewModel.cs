using CommunityToolkit.Mvvm.ComponentModel;
using VideoLab.Core.Media;
using Lab.UI.Controls;
using Lab.Core.Processes;
using Lab.Core.Tools;
using Lab.UI.Services;
using Lab.UI.ViewModels;

namespace VideoLab.UI.ViewModels;

/// <summary>Root view model: composes the suite's Core services and the child view models.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    public MainViewModel()
    {
        var locator = new ToolLocator();
        Tools = new ToolStatusViewModel(locator, ["ffmpeg", "ffprobe"]);
        Terminal = new TerminalViewModel();

        var runner = new ProcessRunner();
        VideoRenderService? renderService = null;
        var ffmpeg = Tools.PathOf("ffmpeg");
        var ffprobe = Tools.PathOf("ffprobe");
        if (ffmpeg is not null && ffprobe is not null)
        {
            renderService = new VideoRenderService(
                runner,
                ffmpeg,
                new FfprobeService(runner, ffprobe),
                new ImageProbeService(runner, ffprobe));
        }

        Render = new VideoRenderViewModel(renderService, new FilePickerService(), Terminal, Tools.AllPresent);
    }

    public ToolStatusViewModel Tools { get; }

    public TerminalViewModel Terminal { get; }

    public VideoRenderViewModel Render { get; }

    public void Dispose() => Render.Dispose();
}
