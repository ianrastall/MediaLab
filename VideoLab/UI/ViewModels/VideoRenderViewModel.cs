using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoLab.Core.Media;
using Lab.UI.Controls;
using Lab.UI.Services;
using Lab.UI.ViewModels;

namespace VideoLab.UI.ViewModels;

/// <summary>
/// Owns the single VideoLab render: input selection, the chosen visualizer color, and the
/// render/cancel lifecycle. A <see cref="VideoRenderJob"/> snapshot is taken at render start so
/// later UI edits cannot mutate an in-flight render.
/// </summary>
public sealed partial class VideoRenderViewModel : ObservableObject, IDisposable
{
    private const string AudioFilter = "Audio (*.flac;*.wav;*.aiff;*.aif)|*.flac;*.wav;*.aiff;*.aif|All files (*.*)|*.*";
    private const string ImageFilter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All files (*.*)|*.*";
    private const string Mp4Filter = "MP4 video (*.mp4)|*.mp4";

    private readonly VideoRenderService? _renderService;
    private readonly IFilePickerService _filePicker;
    private readonly TerminalViewModel _terminal;
    private readonly bool _toolsReady;
    private CancellationTokenSource? _cts;
    private readonly Stopwatch _renderStopwatch = new();
    private DispatcherTimer? _elapsedTimer;

    public VideoRenderViewModel(
        VideoRenderService? renderService,
        IFilePickerService filePicker,
        TerminalViewModel terminal,
        bool toolsReady)
    {
        _renderService = renderService;
        _filePicker = filePicker;
        _terminal = terminal;
        _toolsReady = toolsReady;
        StatusText = toolsReady ? "Ready." : "ffmpeg/ffprobe not found — set them up to render.";
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenderCommand))]
    private string? audioPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenderCommand))]
    private string? imagePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenderCommand))]
    private string? outputPath;

    [ObservableProperty]
    private Color selectedColor = Color.FromRgb(0xC0, 0x18, 0x22);

    [ObservableProperty]
    private Color peakColor = Color.FromRgb(0x86, 0x11, 0x18);

    [ObservableProperty]
    private string peakHex = "#861118";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool isRendering;

    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private string statusText = string.Empty;

    /// <summary>Wall-clock elapsed time, ticking every second during a render.</summary>
    [ObservableProperty]
    private string elapsedText = "00:00";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenOutputFolderCommand))]
    private bool hasRenderedOutput;

    /// <summary>Fixed visualizer opacity indicator (product constant, not user-editable).</summary>
    public string OpacityDisplay => $"{VideoRenderJob.DefaultOpacity * 100:0}%";

    /// <summary>
    /// Static 40-bar pattern (heights in the preview's 1080-tall frame) for the overlay — a
    /// representative descending spectrum so the user sees the bar layout/color, not live audio.
    /// </summary>
    public IReadOnlyList<double> PreviewBars { get; } = BuildPreviewBars();

    private static double[] BuildPreviewBars()
    {
        const double frameHeight = 1080.0;
        var bars = new double[VideoRenderJob.DefaultBarCount];
        var rng = new Random(42); // deterministic look
        for (var i = 0; i < bars.Length; i++)
        {
            var envelope = 0.58 * frameHeight * (1.0 - (i / (double)bars.Length)); // descending left->right
            bars[i] = Math.Clamp(envelope * (0.55 + rng.NextDouble() * 0.45), 24, frameHeight);
        }

        return bars;
    }

    public string SelectedHex => $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";

    partial void OnSelectedColorChanged(Color value)
    {
        var peak = ColorMath.PeakFor(new RgbColor(value.R, value.G, value.B));
        PeakColor = Color.FromRgb(peak.R, peak.G, peak.B);
        PeakHex = peak.ToHex();
        OnPropertyChanged(nameof(SelectedHex));
    }

    partial void OnAudioPathChanged(string? value)
    {
        // Auto-suggest an output path next to the audio when none is set yet.
        if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(OutputPath))
        {
            OutputPath = OutputPathBuilder.ForAudio(value);
        }
    }

    [RelayCommand]
    private void PickAudio()
    {
        var picked = _filePicker.PickOpenFile("Select audio", AudioFilter, DirectoryOf(AudioPath));
        if (picked is not null)
        {
            AudioPath = picked;
        }
    }

    [RelayCommand]
    private void PickImage()
    {
        var picked = _filePicker.PickOpenFile("Select image (must be 3840×2160)", ImageFilter, DirectoryOf(ImagePath));
        if (picked is not null)
        {
            ImagePath = picked;
        }
    }

    [RelayCommand]
    private void PickOutput()
    {
        var suggested = !string.IsNullOrWhiteSpace(AudioPath)
            ? Path.GetFileName(OutputPathBuilder.ForAudio(AudioPath))
            : "output_video.mp4";
        var picked = _filePicker.PickSaveFile("Select output MP4", Mp4Filter, suggested, DirectoryOf(OutputPath ?? AudioPath));
        if (picked is not null)
        {
            OutputPath = picked;
        }
    }

    private bool CanRender() =>
        _toolsReady
        && !IsRendering
        && _renderService is not null
        && !string.IsNullOrWhiteSpace(AudioPath)
        && !string.IsNullOrWhiteSpace(ImagePath)
        && !string.IsNullOrWhiteSpace(OutputPath);

    [RelayCommand(CanExecute = nameof(CanRender))]
    private async Task RenderAsync()
    {
        if (_renderService is null)
        {
            return;
        }

        var job = VideoRenderJob.Create(AudioPath!, ImagePath!, OutputPath!, SelectedHex);

        IsRendering = true;
        HasRenderedOutput = false;
        ProgressPercent = 0;
        StatusText = "Rendering…";
        ElapsedText = "00:00";
        _terminal.Append($"=== Render start: {job.OutputPath} ===");

        // Wall-clock stopwatch that ticks once a second, independent of encode speed.
        _renderStopwatch.Restart();
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += OnElapsedTick;
        _elapsedTimer.Start();

        _cts = new CancellationTokenSource();
        var progress = new Progress<VideoRenderProgress>(p =>
        {
            ProgressPercent = p.Percent;
            var eta = string.Empty;
            if (p.Percent > 1)
            {
                var remaining = TimeSpan.FromSeconds(_renderStopwatch.Elapsed.TotalSeconds * (100 - p.Percent) / p.Percent);
                eta = $"   ETA {FormatDuration(remaining)}";
            }

            StatusText = $"Rendering… {p.Percent:0}%   encoded {p.Position:hh\\:mm\\:ss} / {p.Total:hh\\:mm\\:ss}{eta}";
        });

        try
        {
            var result = await _renderService.RenderAsync(job, progress, _terminal.Append, _cts.Token);
            switch (result.Outcome)
            {
                case VideoRenderOutcome.Success:
                    ProgressPercent = 100;
                    StatusText = "Done.";
                    HasRenderedOutput = true;
                    _terminal.Append($"=== Render complete: {result.OutputPath} ===");
                    break;
                case VideoRenderOutcome.Canceled:
                    StatusText = "Canceled.";
                    _terminal.Append("=== Render canceled ===");
                    break;
                default:
                    StatusText = result.Error ?? "Render failed.";
                    _terminal.Append($"=== Render failed: {result.Error} ===");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Defensive: the service handles cancellation internally, but never let it surface.
            StatusText = "Canceled.";
            _terminal.Append("=== Render canceled ===");
        }
        catch (Exception ex)
        {
            // Any unexpected fault must not crash the app via the async command.
            StatusText = $"Render failed: {ex.Message}";
            _terminal.Append($"=== Render crashed: {ex.Message} ===");
        }
        finally
        {
            _renderStopwatch.Stop();
            if (_elapsedTimer is not null)
            {
                _elapsedTimer.Stop();
                _elapsedTimer.Tick -= OnElapsedTick;
                _elapsedTimer = null;
            }

            IsRendering = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnElapsedTick(object? sender, EventArgs e) =>
        ElapsedText = FormatDuration(_renderStopwatch.Elapsed);

    /// <summary>
    /// Formats a wall-clock duration for the elapsed/ETA readouts. Shows <c>h:mm:ss</c> once the
    /// span reaches an hour and <c>mm:ss</c> below that — a plain <c>mm\:ss</c> format string
    /// renders only the minutes <i>component</i> (0–59), so a 65-minute render would display as
    /// "05:00" and appear to reset at the hour mark.
    /// </summary>
    private static string FormatDuration(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }

        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    private bool CanCancel() => IsRendering;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "Canceling…";
    }

    private bool CanOpenOutputFolder() => HasRenderedOutput && !string.IsNullOrWhiteSpace(OutputPath);

    [RelayCommand(CanExecute = nameof(CanOpenOutputFolder))]
    private void OpenOutputFolder()
    {
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            _filePicker.RevealInExplorer(OutputPath);
        }
    }

    private static string? DirectoryOf(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);

    /// <summary>
    /// Requests cancellation of an in-flight render when the window closes, so ffmpeg gets a
    /// chance to exit and clean up its own .partial file rather than being force-killed at
    /// process teardown. <see cref="ProcessRunner"/>'s job-object kill-on-close is the backstop
    /// if the app is closed forcefully enough that this never gets to run.
    /// </summary>
    public void Dispose() => _cts?.Cancel();
}
