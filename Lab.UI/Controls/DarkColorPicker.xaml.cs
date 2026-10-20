using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;


namespace Lab.UI.Controls;

/// <summary>
/// A dark-themed color picker: saturation/value plane, hue strip, hex entry, live swatch, and
/// recent colors. Exposes a two-way bindable <see cref="SelectedColor"/>.
/// </summary>
public partial class DarkColorPicker : UserControl
{
    private double _hue = 350;       // matches the default accent-ish red
    private double _sat = 0.87;
    private double _val = 0.75;
    private bool _updating;
    private bool _draggingSv;
    private bool _draggingHue;

    public DarkColorPicker()
    {
        InitializeComponent();
        RecentColorsList.ItemsSource = RecentColors;
        Loaded += (_, _) => SyncUiFromHsv();
    }

    /// <summary>Recently committed colors (most recent first).</summary>
    public ObservableCollection<Color> RecentColors { get; } = [];

    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(Color),
            typeof(DarkColorPicker),
            new FrameworkPropertyMetadata(
                Color.FromRgb(0xC0, 0x18, 0x22),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>The selected color as canonical <c>#RRGGBB</c>.</summary>
    public string SelectedHex => $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (DarkColorPicker)d;
        if (picker._updating)
        {
            return;
        }

        var (h, s, v) = ColorConversions.ToHsv((Color)e.NewValue);
        picker._hue = h;
        picker._sat = s;
        picker._val = v;
        picker.SyncUiFromHsv();
    }

    // ----- Saturation/Value plane -----

    private void SvPlane_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingSv = true;
        SvPlane.CaptureMouse();
        UpdateSvFromPoint(e.GetPosition(SvPlane));
    }

    private void SvPlane_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingSv)
        {
            UpdateSvFromPoint(e.GetPosition(SvPlane));
        }
    }

    private void SvPlane_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingSv)
        {
            return;
        }

        _draggingSv = false;
        SvPlane.ReleaseMouseCapture();
        CommitRecent();
    }

    private void SvPlane_SizeChanged(object sender, SizeChangedEventArgs e) => SyncUiFromHsv();

    private void UpdateSvFromPoint(Point p)
    {
        var w = SvPlane.ActualWidth;
        var h = SvPlane.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        _sat = Math.Clamp(p.X / w, 0, 1);
        _val = Math.Clamp(1 - (p.Y / h), 0, 1);
        PushColorFromHsv();
    }

    // ----- Hue strip -----

    private void HueStrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = true;
        HueStrip.CaptureMouse();
        UpdateHueFromPoint(e.GetPosition(HueStrip));
    }

    private void HueStrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingHue)
        {
            UpdateHueFromPoint(e.GetPosition(HueStrip));
        }
    }

    private void HueStrip_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingHue)
        {
            return;
        }

        _draggingHue = false;
        HueStrip.ReleaseMouseCapture();
        CommitRecent();
    }

    private void UpdateHueFromPoint(Point p)
    {
        var h = HueStrip.ActualHeight;
        if (h <= 0)
        {
            return;
        }

        _hue = Math.Clamp(p.Y / h, 0, 1) * 360;
        PushColorFromHsv();
    }

    // ----- Hex entry -----

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
        }
    }

    private void HexBox_LostFocus(object sender, RoutedEventArgs e) => CommitHex();

    private void CommitHex()
    {
        if (ColorMath.TryParse(HexBox.Text, out var rgb))
        {
            _updating = true;
            SelectedColor = Color.FromRgb(rgb.R, rgb.G, rgb.B);
            _updating = false;
            var (h, s, v) = ColorConversions.ToHsv(SelectedColor);
            _hue = h;
            _sat = s;
            _val = v;
            SyncUiFromHsv();
            CommitRecent();
        }
        else
        {
            // Revert the text box to the current valid color.
            HexBox.Text = SelectedHex;
        }
    }

    private void RecentColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Color color })
        {
            SelectedColor = color; // OnSelectedColorChanged re-syncs HSV + UI
        }
    }

    // ----- Shared UI sync -----

    private void PushColorFromHsv()
    {
        _updating = true;
        SelectedColor = ColorConversions.FromHsv(_hue, _sat, _val);
        _updating = false;
        SyncUiFromHsv();
    }

    private void SyncUiFromHsv()
    {
        SvHueLayer.Fill = new SolidColorBrush(ColorConversions.HueColor(_hue));
        SwatchBrush.Color = SelectedColor;
        if (!HexBox.IsFocused)
        {
            HexBox.Text = SelectedHex;
        }

        var w = SvPlane.ActualWidth;
        var h = SvPlane.ActualHeight;
        if (w > 0 && h > 0)
        {
            Canvas.SetLeft(SvThumb, (_sat * w) - (SvThumb.Width / 2));
            Canvas.SetTop(SvThumb, ((1 - _val) * h) - (SvThumb.Height / 2));
            SvThumb.Fill = new SolidColorBrush(SelectedColor);
        }

        var hh = HueStrip.ActualHeight;
        if (hh > 0)
        {
            Canvas.SetTop(HueThumb, (_hue / 360 * hh) - (HueThumb.Height / 2));
        }
    }

    private void CommitRecent()
    {
        var color = SelectedColor;
        var existing = RecentColors.IndexOf(color);
        if (existing >= 0)
        {
            RecentColors.Move(existing, 0);
            return;
        }

        RecentColors.Insert(0, color);
        while (RecentColors.Count > 10)
        {
            RecentColors.RemoveAt(RecentColors.Count - 1);
        }
    }
}
