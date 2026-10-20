using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lab.Core.Tools;

namespace SongLab.UI.ViewModels;

public sealed partial class SetupViewModel : ObservableObject
{
    private readonly FfmpegInstallerService _ffmpegInstaller = new();
    private readonly ToolLocator _locator = new();
    private readonly string _installDir = ToolDefaults.PortableRoot;
    private CancellationTokenSource? _cts;

    public string InstallDir => _installDir;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallFfmpegCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallSoxCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallRubberbandCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallRsgainCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string progressText = string.Empty;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private string logText = string.Empty;

    // Status properties for each tool
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallFfmpegCommand))]
    private bool ffmpegInstalled;
    [ObservableProperty] private string ffmpegStatusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSoxCommand))]
    private bool soxInstalled;
    [ObservableProperty] private string soxStatusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallRubberbandCommand))]
    private bool rubberbandInstalled;
    [ObservableProperty] private string rubberbandStatusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallRsgainCommand))]
    private bool rsgainInstalled;
    [ObservableProperty] private string rsgainStatusText = string.Empty;

    public SetupViewModel()
    {
        Refresh();
    }

    public void Refresh()
    {
        // FFmpeg
        FfmpegInstalled = FfmpegInstallerService.IsFfmpegInstalled(_installDir) || _locator.Locate("ffmpeg") != null;
        FfmpegStatusText = FfmpegInstalled ? "ffmpeg found" : "ffmpeg not found";

        // SoX
        SoxInstalled = _locator.Locate("sox") != null;
        SoxStatusText = SoxInstalled ? "sox found" : "sox not found";

        // Rubber Band
        RubberbandInstalled = _locator.Locate("rubberband-r3") != null;
        RubberbandStatusText = RubberbandInstalled ? "rubberband-r3 found" : "rubberband-r3 not found";

        // rsgain
        RsgainInstalled = _locator.Locate("rsgain") != null;
        RsgainStatusText = RsgainInstalled ? "rsgain found" : "rsgain not found";
    }

    private bool CanInstallFfmpeg() => !IsBusy && !FfmpegInstalled;
    [RelayCommand(CanExecute = nameof(CanInstallFfmpeg))]
    private async Task InstallFfmpegAsync() => await RunDownloadAsync(
        () => _ffmpegInstaller.InstallFfmpegAsync(Path.Combine(_installDir, "ffmpeg"), AppendLog, MakeProgress("Downloading FFmpeg"), _cts!.Token, null),
        "FFmpeg installed.");

    private bool CanInstallSox() => !IsBusy && !SoxInstalled;
    [RelayCommand(CanExecute = nameof(CanInstallSox))]
    private async Task InstallSoxAsync() => await RunDownloadAsync(
        () => new ZipInstallerService().InstallFromZipAsync(
            "https://downloads.sourceforge.net/project/sox/sox/14.4.2/sox-14.4.2-win32.zip",
            Path.Combine(_installDir, "sox"), ["sox.exe", "*.dll"], AppendLog, MakeProgress("Downloading SoX"), _cts!.Token, "8072cc147cf1a3b3713b8b97d6844bb9389e211ab9e1101e432193fad6ae6662"),
        "SoX installed.");

    private bool CanInstallRubberband() => !IsBusy && !RubberbandInstalled;
    [RelayCommand(CanExecute = nameof(CanInstallRubberband))]
    private async Task InstallRubberbandAsync() => await RunDownloadAsync(
        () => new ZipInstallerService().InstallFromZipAsync(
            "https://breakfastquay.com/files/releases/rubberband-3.3.0-gpl-executable-windows.zip",
            Path.Combine(_installDir, "rubberband"), ["rubberband-r3.exe", "sndfile.dll"], AppendLog, MakeProgress("Downloading Rubber Band"), _cts!.Token, "03fed923085d1dec42f1d75a5b189c011ee2439b32bbecfe18e45c530214dfca"),
        "Rubber Band installed.");

    private bool CanInstallRsgain() => !IsBusy && !RsgainInstalled;
    [RelayCommand(CanExecute = nameof(CanInstallRsgain))]
    private async Task InstallRsgainAsync() => await RunDownloadAsync(
        () => new ZipInstallerService().InstallFromZipAsync(
            "https://github.com/complexlogic/rsgain/releases/download/v3.7/rsgain-3.7-win64.zip",
            Path.Combine(_installDir, "rsgain"), ["rsgain.exe"], AppendLog, MakeProgress("Downloading rsgain"), _cts!.Token, "8d6e16d1af5a805fb2dd6183e11f4f8bc51240f8a896fd37ab8fca655bb70800"),
        "rsgain installed.");

    private async Task RunDownloadAsync(Func<Task> action, string successMessage)
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        ProgressValue = 0;
        LogText = string.Empty;

        try
        {
            await action();
            ProgressText = successMessage;
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Canceled.";
        }
        catch (Exception ex)
        {
            ProgressText = $"Failed: {ex.Message}";
            AppendLog($"[error] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            Refresh();
        }
    }

    private void AppendLog(string line) => LogText = string.IsNullOrEmpty(LogText) ? line : LogText + "\n" + line;

    private IProgress<double> MakeProgress(string prefix)
    {
        ProgressText = prefix + "...";
        return new Progress<double>(v =>
        {
            ProgressValue = v;
            ProgressText = $"{prefix}... {v:P0}";
        });
    }

    [RelayCommand]
    private void CancelDownload() => _cts?.Cancel();

    [RelayCommand]
    private void OpenInstallFolder()
    {
        Directory.CreateDirectory(_installDir);
        Process.Start("explorer.exe", _installDir);
    }
}
