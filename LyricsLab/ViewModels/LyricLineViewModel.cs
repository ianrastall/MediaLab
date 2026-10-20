using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace LyricsLab.ViewModels
{
    public partial class LyricLineViewModel : ObservableObject
    {
        [ObservableProperty]
        private string text = string.Empty;

        [ObservableProperty]
        private TimeSpan startTime;

        [ObservableProperty]
        private TimeSpan endTime;

        [ObservableProperty]
        private bool isActive;

        [ObservableProperty]
        private bool isAnchor;

        [ObservableProperty]
        private double? distanceNext;
    }
}
