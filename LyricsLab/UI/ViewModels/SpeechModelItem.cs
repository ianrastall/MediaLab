using CommunityToolkit.Mvvm.ComponentModel;
using LyricsLab.Core.Lyrics;

namespace LyricsLab.UI.ViewModels;

/// <summary>UI wrapper around <see cref="SpeechModelInfo"/> with live install/download state.</summary>
public sealed partial class SpeechModelItem : ObservableObject
{
    public SpeechModelInfo Info { get; }

    [ObservableProperty]
    private bool isInstalled;

    [ObservableProperty]
    private bool isDownloading;

    public SpeechModelItem(SpeechModelInfo info, bool installed)
    {
        Info = info;
        isInstalled = installed;
    }

    public string SizeMbText => $"{Info.SizeMb} MB";

    /// <summary>Display label for the Engine column.</summary>
    public string EngineName => Info.Engine.ToString();

    public bool IsRecommended => Info.IsRecommended;
    public bool IsWarning => Info.IsWarning;
}
