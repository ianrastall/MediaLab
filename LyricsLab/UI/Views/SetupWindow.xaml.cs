using System.Windows;
using LyricsLab.UI.ViewModels;

namespace LyricsLab.UI.Views;

public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
        DataContext = new SetupViewModel();
    }
}
