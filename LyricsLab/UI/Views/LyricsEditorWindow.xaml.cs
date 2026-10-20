using System.Windows;
using System.Windows.Controls;
using LyricsLab.UI.ViewModels;

namespace LyricsLab.UI.Views;

public partial class LyricsEditorWindow : Window
{
    private readonly LyricsEditorViewModel _vm;

    /// <summary>The generated XML set when the user clicks Apply, null if cancelled.</summary>
    public string? Result { get; private set; }

    public LyricsEditorWindow(
        string initialLyricsText, 
        string? audioFilePath = null, 
        LyricsLab.Core.Lyrics.OracleTranscriberService? oracle = null, 
        LyricsLab.Core.Lyrics.InstrumentalDetectorService? vadDetector = null)
    {
        InitializeComponent();
        _vm = new LyricsEditorViewModel();
        DataContext = _vm;
        _vm.LoadFrom(initialLyricsText);
        
        _vm.InitDependencies(
            audioFilePath, 
            oracle, 
            vadDetector, 
            msg => System.Windows.Application.Current.Dispatcher.Invoke(() => _vm.StatusText = msg));
    }

    private void SourceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _vm.SetSelection(SourceListBox.SelectedItems);

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        Result = _vm.GenerateXml();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private void CopyXml_Click(object sender, RoutedEventArgs e)
    {
        var xml = _vm.GenerateXml();
        Clipboard.SetText(xml);
        MessageBox.Show("XML copied to clipboard.", "LyricsLab", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var xml = _vm.GenerateXml();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Lyrics XML (*.xml)|*.xml",
            DefaultExt = ".xml",
            FileName = "lyrics.xml"
        };
        if (dlg.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dlg.FileName, xml);
            MessageBox.Show($"Saved to {System.IO.Path.GetFileName(dlg.FileName)}", "LyricsLab", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private bool _isDraggingSelection;
    private ListBoxItem? _dragStartItem;

    private void SourceListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = GetListBoxItemUnderMouse(e.GetPosition(SourceListBox));
        if (item != null)
        {
            _isDraggingSelection = true;
            _dragStartItem = item;
            
            if (!System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
            {
                SourceListBox.SelectedItems.Clear();
            }
            
            item.IsSelected = true;
            System.Windows.Input.Mouse.Capture(SourceListBox);
        }
    }

    private void SourceListBox_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingSelection)
        {
            var item = GetListBoxItemUnderMouse(e.GetPosition(SourceListBox));
            if (item != null && !item.IsSelected)
            {
                item.IsSelected = true;
            }
        }
    }

    private void SourceListBox_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isDraggingSelection)
        {
            _isDraggingSelection = false;
            _dragStartItem = null;
            System.Windows.Input.Mouse.Capture(null);
        }
    }

    private ListBoxItem? GetListBoxItemUnderMouse(Point p)
    {
        var hitTestResult = System.Windows.Media.VisualTreeHelper.HitTest(SourceListBox, p);
        if (hitTestResult == null) return null;

        DependencyObject obj = hitTestResult.VisualHit;
        while (obj != null && obj != SourceListBox)
        {
            if (obj is ListBoxItem item) return item;
            obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
        }
        return null;
    }
}
