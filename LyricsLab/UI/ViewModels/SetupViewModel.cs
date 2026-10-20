using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LyricsLab.Core.Lyrics;
using Lab.Core.Tools;

namespace LyricsLab.UI.ViewModels;

/// <summary>
/// Drives the speech engine setup window: whisper-cli binary, Whisper ggml models, Vosk models.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly WhisperInstallerService _installer = new();
    private readonly FfmpegInstallerService _ffmpegInstaller = new();
    private readonly string _installDir = WhisperInstallerService.DefaultInstallDir;
    private readonly string _ffmpegInstallDir = FfmpegInstallerService.DefaultInstallDir;
    private CancellationTokenSource? _cts;



    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallWhisperBinaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallWhisperXCommand))]
    private bool isBusy;

    [ObservableProperty]
    private bool whisperInstalled;

    [ObservableProperty]
    private string whisperStatusText = string.Empty;

    [ObservableProperty]
    private bool whisperxInstalled;

    [ObservableProperty]
    private string whisperxStatusText = string.Empty;

    [ObservableProperty]
    private bool ffmpegInstalled;

    [ObservableProperty]
    private string ffmpegStatusText = string.Empty;

    [ObservableProperty]
    private bool speechModelsInstalled;

    [ObservableProperty]
    private string speechModelsStatusText = string.Empty;

    [ObservableProperty]
    private string progressText = string.Empty;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private string logText = string.Empty;

    public string InstallDir => _installDir;

    public SetupViewModel() => Refresh();

    // ─────────────────────────────────────────────────────────────────────────
    // Status refresh
    // ─────────────────────────────────────────────────────────────────────────

    public void Refresh()
    {
        WhisperInstalled = WhisperInstallerService.IsWhisperBinaryInstalled(_installDir);
        WhisperStatusText = WhisperInstalled
            ? $"whisper-cli.exe found in {_installDir}"
            : $"whisper-cli.exe not found in {_installDir}";

        WhisperxInstalled = WhisperInstallerService.IsWhisperXInstalled(_installDir);
        WhisperxStatusText = WhisperxInstalled
            ? $"whisperX venv found in {_installDir}"
            : $"whisperX not found in {_installDir} (requires ~6 GB)";

        FfmpegInstalled = FfmpegInstallerService.IsFfmpegInstalled(_ffmpegInstallDir);
        FfmpegStatusText = FfmpegInstalled
            ? $"ffmpeg found in {_ffmpegInstallDir}"
            : $"ffmpeg not found in {_ffmpegInstallDir}";

        var whisperModels = WhisperInstallerService.InstalledWhisperModels(_installDir);
        var voskModels    = WhisperInstallerService.InstalledVoskModels(_installDir);

        var hasWhisperSmall = whisperModels.Contains("ggml-small.en.bin");
        var hasVoskSmall = voskModels.Contains("vosk-model-small-en-us-0.15");

        SpeechModelsInstalled = hasWhisperSmall && hasVoskSmall;
        SpeechModelsStatusText = SpeechModelsInstalled
            ? $"Required speech models found in {_installDir}"
            : "Required speech models missing (Whisper small, Vosk small)";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Commands
    // ─────────────────────────────────────────────────────────────────────────

    private bool CanInstallWhisperBinary() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanInstallWhisperBinary))]
    private async Task InstallWhisperBinaryAsync()
    {
        await RunDownloadAsync(
            () => _installer.InstallWhisperBinaryAsync(
                _installDir, AppendLog, MakeProgress("Downloading whisper-cli"), _cts!.Token),
            "Binary installed.");
    }

    private bool CanInstallWhisperX() => !IsBusy && !WhisperxInstalled;

    [RelayCommand(CanExecute = nameof(CanInstallWhisperX))]
    private async Task InstallWhisperXAsync()
    {
        await RunDownloadAsync(
            () => _installer.InstallWhisperXAsync(
                _installDir, AppendLog, _cts!.Token),
            "whisperX installed.");
    }

    private bool CanInstallFfmpeg() => !IsBusy && !FfmpegInstalled;

    [RelayCommand(CanExecute = nameof(CanInstallFfmpeg))]
    private async Task InstallFfmpegAsync()
    {
        await RunDownloadAsync(
            () => _ffmpegInstaller.InstallFfmpegAsync(
                _ffmpegInstallDir, AppendLog, MakeProgress("Downloading FFmpeg"), _cts!.Token),
            "FFmpeg installed.");
    }

    private bool CanInstallSpeechModels() => !IsBusy && !SpeechModelsInstalled;

    [RelayCommand(CanExecute = nameof(CanInstallSpeechModels))]
    private async Task InstallSpeechModelsAsync()
    {
        await RunDownloadAsync(async () =>
        {
            var whisperSmall = WhisperInstallerService.KnownModels.First(m => m.FileName == "ggml-small.en.bin");
            var voskSmall = WhisperInstallerService.KnownModels.First(m => m.FileName == "vosk-model-small-en-us-0.15.zip");

            await _installer.DownloadModelAsync(whisperSmall, _installDir, AppendLog, MakeProgress("Downloading Whisper model"), _cts!.Token);
            await _installer.DownloadModelAsync(voskSmall, _installDir, AppendLog, MakeProgress("Downloading Vosk model"), _cts!.Token);
        }, "Speech models installed.");
    }

    [RelayCommand]
    private void CancelDownload() => _cts?.Cancel();

    [RelayCommand]
    private void OpenInstallFolder()
    {
        Directory.CreateDirectory(_installDir);
        Process.Start("explorer.exe", _installDir);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunDownloadAsync(
        Func<Task> action, string successMessage, Action? onSuccess = null)
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        ProgressValue = 0;
        LogText = string.Empty;

        try
        {
            await action();
            ProgressText = successMessage;
            onSuccess?.Invoke();
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

    private IProgress<double> MakeProgress(string prefix)
    {
        ProgressText = prefix + "…";
        return new Progress<double>(v =>
        {
            ProgressValue = v;
            ProgressText  = $"{prefix}… {v:P0}";
        });
    }

    private void AppendLog(string line) =>
        LogText = string.IsNullOrEmpty(LogText) ? line : LogText + "\n" + line;
}
