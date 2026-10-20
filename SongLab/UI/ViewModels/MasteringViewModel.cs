using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lab.UI.Services;
using Lab.UI.ViewModels;

namespace SongLab.UI.ViewModels;

/// <summary>
/// SongLab's mastering surface. The control set is in place and consistent with the suite; the
/// processing pipeline (decode → SoX DSP → Rubber Band R3 → rsgain → encode) is stubbed pending
/// the Phase 3 audit-driven implementation. File pickers, tool detection, and logging are live.
/// </summary>
public sealed partial class MasteringViewModel : ObservableObject
{
    private const string AudioFilter = "Audio (*.flac;*.wav;*.aiff;*.aif;*.mp3;*.m4a)|*.flac;*.wav;*.aiff;*.aif;*.mp3;*.m4a|All files (*.*)|*.*";

    private readonly IFilePickerService _filePicker;
    private readonly TerminalViewModel _terminal;
    private readonly SongLab.Core.Media.MasteringPipeline _pipeline;

    public MasteringViewModel(IFilePickerService filePicker, TerminalViewModel terminal, ToolStatusViewModel tools, SongLab.Core.Media.MasteringPipeline pipeline)
    {
        _filePicker = filePicker;
        _terminal = terminal;
        _pipeline = pipeline;
        
        toolsReady = tools.AllPresent;
        tools.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(ToolStatusViewModel.AllPresent))
            {
                ToolsReady = tools.AllPresent;
            }
        };

        StatusText = toolsReady ? "Ready." : "Some mastering tools were not found.";
        selectedPreset = Presets[0];
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool toolsReady;

    partial void OnToolsReadyChanged(bool value)
    {
        if (!IsRunning)
            StatusText = value ? "Ready." : "Some mastering tools were not found.";
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePreviewCommand))]
    private string? sourcePath;

    [ObservableProperty]
    private string? outputPath;

    public ObservableCollection<string> DeliveryFormats { get; } = ["24-bit FLAC", "WAV (24-bit)", "AIFF (24-bit)"];

    [ObservableProperty]
    private string selectedFormat = "24-bit FLAC";

    partial void OnSelectedFormatChanged(string value)
    {
        UpdateOutputPath();
    }

    [ObservableProperty]
    private double tempoPercent = 100;          // Rubber Band R3 --fine

    public ObservableCollection<Models.EqPreset> Presets { get; } = new()
    {
        new Models.EqPreset("Custom / Flat", 0, 0, 0, 0),
        new Models.EqPreset("Subtle Warmth", 30, 18000, 2, -1),
        new Models.EqPreset("Vocal/Speech Clarity", 80, 16000, -2, 3),
        new Models.EqPreset("Rumble Removal", 100, 0, -4, 0),
        new Models.EqPreset("Club / Bass Boost", 20, 0, 5, 1)
    };

    [ObservableProperty]
    private Models.EqPreset selectedPreset;

    partial void OnSelectedPresetChanged(Models.EqPreset value)
    {
        if (value.Name == "Custom / Flat") return;
        HighPassHz = value.HighPassHz;
        LowPassHz = value.LowPassHz;
        BassDb = value.BassDb;
        TrebleDb = value.TrebleDb;
    }

    [ObservableProperty]
    private double highPassHz;                   // 0 = off

    [ObservableProperty]
    private double lowPassHz;                    // 0 = off

    [ObservableProperty]
    private double bassDb;

    [ObservableProperty]
    private double trebleDb;

    // Detect manual overrides and set preset to custom
    partial void OnHighPassHzChanged(double value) => CheckCustomPreset();
    partial void OnLowPassHzChanged(double value) => CheckCustomPreset();
    partial void OnBassDbChanged(double value) => CheckCustomPreset();
    partial void OnTrebleDbChanged(double value) => CheckCustomPreset();

    private void CheckCustomPreset()
    {
        if (SelectedPreset?.Name != "Custom / Flat" &&
            (SelectedPreset?.HighPassHz != HighPassHz ||
             SelectedPreset?.LowPassHz != LowPassHz ||
             SelectedPreset?.BassDb != BassDb ||
             SelectedPreset?.TrebleDb != TrebleDb))
        {
            SelectedPreset = Presets[0];
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool applyLoudness = true;

    [ObservableProperty]
    private double loudnessTargetLufs = -14;     // YouTube normalizes down to ~-14 LUFS

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool isRunning;

    [ObservableProperty]
    private string statusText = string.Empty;

    partial void OnSourcePathChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            UpdateOutputPath();
            try { _player.Open(new System.Uri(value)); } catch { }
        }
    }

    partial void OnTempoPercentChanged(double value)
    {
        UpdateOutputPath();
    }

    private void UpdateOutputPath()
    {
        if (string.IsNullOrWhiteSpace(SourcePath)) return;
        var dir = Path.GetDirectoryName(SourcePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(SourcePath);
        var ext = SelectedFormat.Contains("AIFF") ? ".aiff"
                : SelectedFormat.Contains("WAV") ? ".wav"
                : ".flac";
        int tempoDiff = (int)System.Math.Round(TempoPercent - 100);
        var suffix = tempoDiff == 0 ? "_master" : $"_{(tempoDiff > 0 ? "+" : "")}{tempoDiff}pct";
        OutputPath = Path.Combine(dir, $"{stem}{suffix}{ext}");
    }

    private readonly System.Windows.Media.MediaPlayer _player = new();

    [ObservableProperty]
    private bool isPreviewPlaying;

    private bool CanTogglePreview() => !string.IsNullOrWhiteSpace(SourcePath);

    [RelayCommand(CanExecute = nameof(CanTogglePreview))]
    private void TogglePreview()
    {
        if (IsPreviewPlaying)
        {
            _player.Pause();
            IsPreviewPlaying = false;
        }
        else
        {
            _player.Play();
            IsPreviewPlaying = true;
        }
    }

    [RelayCommand]
    private void PickSource()
    {
        var picked = _filePicker.PickOpenFile("Select source audio", AudioFilter, DirectoryOf(SourcePath));
        if (picked is not null)
        {
            SourcePath = picked;
        }
    }

    [RelayCommand]
    private void PickOutput()
    {
        var picked = _filePicker.PickSaveFile("Select output", "FLAC (*.flac)|*.flac|WAV (*.wav)|*.wav|AIFF (*.aiff)|*.aiff",
            SourcePath is null ? null : Path.GetFileNameWithoutExtension(SourcePath) + "_master.flac", DirectoryOf(OutputPath ?? SourcePath));
        if (picked is not null)
        {
            OutputPath = picked;
        }
    }

    private bool CanRun() => ToolsReady && !IsRunning && !string.IsNullOrWhiteSpace(SourcePath);

    private System.Threading.CancellationTokenSource? _runCts;

    [RelayCommand]
    private void Cancel()
    {
        _runCts?.Cancel();
        _runCts = null;
        StatusText = "Cancelled.";
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async System.Threading.Tasks.Task RunAsync()
    {
        if (SourcePath is null || OutputPath is null) return;
        
        _runCts?.Cancel();
        _runCts = new System.Threading.CancellationTokenSource();
        var ct = _runCts.Token;

        IsRunning = true;
        _terminal.Append("SongLab: Starting Phase 3 mastering pipeline...");
        _terminal.Append($"  source={SourcePath}");
        _terminal.Append($"  output={OutputPath}  format={SelectedFormat}");
        _terminal.Append($"  tempo={TempoPercent:0}%  preset={SelectedPreset.Name} HP={HighPassHz:0}Hz  LP={LowPassHz:0}Hz  bass={BassDb:0}dB  treble={TrebleDb:0}dB");
        _terminal.Append($"  loudness={(ApplyLoudness ? LoudnessTargetLufs.ToString("0.#") + " LUFS" : "off")}");

        try
        {
            var formatEnum = SelectedFormat.Contains("AIFF") ? SongLab.Core.Media.DeliveryFormat.Aiff
                           : SelectedFormat.Contains("WAV") ? SongLab.Core.Media.DeliveryFormat.Wav
                           : SongLab.Core.Media.DeliveryFormat.Flac;

            await _pipeline.RunMasteringAsync(
                SourcePath,
                OutputPath,
                formatEnum,
                TempoPercent,
                HighPassHz,
                LowPassHz,
                BassDb,
                TrebleDb,
                ApplyLoudness,
                LoudnessTargetLufs,
                msg => {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => StatusText = msg);
                    _terminal.Append("  " + msg);
                },
                cancellationToken: ct);
                
            _terminal.Append("SongLab: Mastering finished successfully.");
            StatusText = "Mastering finished successfully.";
        }
        catch (System.OperationCanceledException)
        {
            _terminal.Append("SongLab: Mastering cancelled.");
            StatusText = "Cancelled.";
        }
        catch (System.Exception ex)
        {
            _terminal.Append($"SongLab Error: {ex.Message}");
            StatusText = "Mastering failed.";
        }
        finally
        {
            _runCts = null;
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void OpenInVideoLab()
    {
        // Phase 5: launch VideoLab with --audio <output> handoff.
        _terminal.Append("Handoff to VideoLab is a Phase 5 feature (not wired yet).");
    }

    private static string? DirectoryOf(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
}
