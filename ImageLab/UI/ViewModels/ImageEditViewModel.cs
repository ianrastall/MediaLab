using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLab.Core.Imaging;
using Lab.UI.Services;
using Lab.UI.ViewModels;

namespace ImageLab.UI.ViewModels;

/// <summary>
/// ImageLab's single-image editor: crop, resize/canvas, brightness, contrast, saturation, chroma,
/// and per-channel RGB — applied by ImageMagick. Adjustments drive a debounced, downscaled live
/// preview rendered by the same operators as the export, so what you see matches what you get.
/// </summary>
public sealed partial class ImageEditViewModel : ObservableObject
{
    private const string ImageFilter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp|All files (*.*)|*.*";
    private static readonly string PreviewPath = Path.Combine(Path.GetTempPath(), "imagelab_preview.png");

    private readonly ImageOpsService? _ops;
    private readonly IFilePickerService _filePicker;
    private readonly TerminalViewModel _terminal;
    private readonly bool _toolsReady;
    private readonly DispatcherTimer _previewTimer;
    private CancellationTokenSource? _previewCts;

    public ImageEditViewModel(ImageOpsService? ops, IFilePickerService filePicker, TerminalViewModel terminal, bool toolsReady)
    {
        _ops = ops;
        _filePicker = filePicker;
        _terminal = terminal;
        _toolsReady = toolsReady;
        StatusText = toolsReady ? "Open an image to begin." : "ImageMagick (magick) not found.";

        _previewTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(220) };
        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await RenderPreviewAsync(); };
    }

    public ObservableCollection<ResizeMode> ResizeModes { get; } =
        [ResizeMode.Fill, ResizeMode.Fit, ResizeMode.Stretch, ResizeMode.None];

    [ObservableProperty] private string? sourcePath;
    [ObservableProperty] private string? outputPath;

    [ObservableProperty] private bool cropEnabled;
    [ObservableProperty] private int cropX;
    [ObservableProperty] private int cropY;
    [ObservableProperty] private int cropWidth = 3840;
    [ObservableProperty] private int cropHeight = 2160;

    [ObservableProperty] private int brightness;
    [ObservableProperty] private int contrast;
    [ObservableProperty] private double gamma = 1.0;
    [ObservableProperty] private int saturation = 100;
    [ObservableProperty] private int chroma = 100;          // hue rotation; labeled "Chroma" in the UI
    [ObservableProperty] private int red;
    [ObservableProperty] private int green;
    [ObservableProperty] private int blue;

    [ObservableProperty] private ResizeMode selectedResizeMode = ResizeMode.Fill;
    [ObservableProperty] private int targetWidth = 3840;
    [ObservableProperty] private int targetHeight = 2160;

    [ObservableProperty] private ImageSource? previewSource;
    [ObservableProperty] private string statusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool isBusy;

    public string SourceDimensionsText { get; private set; } = string.Empty;

    private ImageAdjustments CurrentAdjustments() => new()
    {
        Crop = CropEnabled ? new CropRect(CropX, CropY, CropWidth, CropHeight) : null,
        Brightness = Brightness,
        Contrast = Contrast,
        Gamma = Gamma,
        Saturation = Saturation,
        Chroma = Chroma,
        Red = Red,
        Green = Green,
        Blue = Blue,
        ResizeMode = SelectedResizeMode,
        TargetWidth = TargetWidth,
        TargetHeight = TargetHeight,
    };

    // Any adjustment change re-renders the preview (debounced). Exclude output-only props.
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(PreviewSource):
            case nameof(StatusText):
            case nameof(IsBusy):
            case nameof(OutputPath):
                break;
            default:
                SchedulePreview();
                break;
        }
    }

    partial void OnSourcePathChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(OutputPath))
        {
            var dir = Path.GetDirectoryName(value) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(value);
            OutputPath = Path.Combine(dir, $"{stem}_4k.png");
        }
    }

    private void SchedulePreview()
    {
        if (!_toolsReady || _ops is null || string.IsNullOrWhiteSpace(SourcePath))
        {
            return;
        }

        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async Task RenderPreviewAsync()
    {
        if (_ops is null || string.IsNullOrWhiteSpace(SourcePath))
        {
            return;
        }

        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        try
        {
            var path = await _ops.RenderPreviewAsync(SourcePath, CurrentAdjustments(), PreviewPath, 960, token).ConfigureAwait(true);
            if (token.IsCancellationRequested || path is null)
            {
                return;
            }

            PreviewSource = LoadUnlocked(path);
            StatusText = "Preview updated.";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer preview request.
        }
    }

    private static ImageSource LoadUnlocked(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;          // read fully, then release the file
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // never serve a stale cached frame
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    [RelayCommand]
    private async Task PickSource()
    {
        var picked = _filePicker.PickOpenFile("Select image", ImageFilter, DirectoryOf(SourcePath));
        if (picked is null)
        {
            return;
        }

        SourcePath = picked;
        if (_ops is not null)
        {
            var dims = await _ops.GetDimensionsAsync(picked);
            SourceDimensionsText = dims is { } d ? $"Source: {d}" : string.Empty;
            OnPropertyChanged(nameof(SourceDimensionsText));
        }
    }

    [RelayCommand]
    private void PickOutput()
    {
        var picked = _filePicker.PickSaveFile("Save image as", "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg",
            SourcePath is null ? "image_4k.png" : Path.GetFileNameWithoutExtension(SourcePath) + "_4k.png",
            DirectoryOf(OutputPath ?? SourcePath));
        if (picked is not null)
        {
            OutputPath = picked;
        }
    }

    private bool CanExport() => _toolsReady && _ops is not null && !IsBusy
        && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(OutputPath);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        if (_ops is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Exporting…";
        _terminal.Append($"ImageLab: exporting {OutputPath}");
        try
        {
            var ok = await _ops.ExportAsync(SourcePath!, CurrentAdjustments(), OutputPath!);
            StatusText = ok ? "Exported." : "Export failed.";
            _terminal.Append(ok ? $"  done: {OutputPath}" : "  export failed.");
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
            _terminal.Append($"  export crashed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenInVideoLab() =>
        _terminal.Append("Handoff to VideoLab is a Phase 5 feature (not wired yet).");

    private static string? DirectoryOf(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
}
