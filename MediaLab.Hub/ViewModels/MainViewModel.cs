using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MediaLab.Hub.ViewModels;

public sealed partial class AppTile : ObservableObject
{
    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private string iconPath = string.Empty;

    [ObservableProperty]
    private string executableName = string.Empty;

    public AppTile(string title, string description, string iconPath, string executableName)
    {
        Title = title;
        Description = description;
        IconPath = iconPath;
        ExecutableName = executableName;
    }
}

public sealed partial class MainViewModel : ObservableObject
{
    public ObservableCollection<AppTile> Apps { get; } = new();

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public MainViewModel()
    {
        Apps.Add(new AppTile(
            "SongLab",
            "Audio mastering and time-stretching.",
            "pack://application:,,,/Resources/icon-song-lab.png",
            "SongLab.exe"
        ));
        
        Apps.Add(new AppTile(
            "LyricsLab",
            "Audio transcription and lyric alignment.",
            "pack://application:,,,/Resources/icon-lyricslab.png",
            "LyricsLab.exe"
        ));
        
        Apps.Add(new AppTile(
            "VideoLab",
            "Video timeline processing and alignment.",
            "pack://application:,,,/Resources/icon-videolab.png",
            "VideoLab.exe"
        ));
        
        Apps.Add(new AppTile(
            "ImageLab",
            "Image cropping and thumbnail generation.",
            "pack://application:,,,/Resources/icon-imagelab.png",
            "ImageLab.exe"
        ));
    }

    [RelayCommand]
    private void LaunchApp(AppTile tile)
    {
        if (tile == null) return;

        try
        {
            // Apps are built into neighboring folders: Build\<AppName>\<AppName>.exe
            var appName = Path.GetFileNameWithoutExtension(tile.ExecutableName);
            var baseDir = AppContext.BaseDirectory;
            
            // Expected path: if Hub is at Build\MediaLab.Hub\, then SongLab is at Build\SongLab\
            var parentBuildDir = Directory.GetParent(baseDir.TrimEnd('\\', '/'))?.FullName;
            if (parentBuildDir == null)
            {
                StatusMessage = "Error: Could not determine base build directory.";
                return;
            }

            var targetExe = Path.Combine(parentBuildDir, appName, tile.ExecutableName);

            if (File.Exists(targetExe))
            {
                StatusMessage = $"Launching {tile.Title}...";
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = Path.GetDirectoryName(targetExe),
                    UseShellExecute = true
                };
                
                Process.Start(startInfo);
            }
            else
            {
                StatusMessage = $"Executable not found at:\n{targetExe}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to launch {tile.Title}: {ex.Message}";
        }
    }
}
