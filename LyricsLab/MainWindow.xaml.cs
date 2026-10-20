using System;
using System.Windows;
using LyricsLab.ViewModels;

namespace LyricsLab
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (DataContext is MainViewModel vm)
            {
                vm.Dispose();
            }
        }
    }
}
