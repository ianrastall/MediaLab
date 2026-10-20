using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;

using LyricsLab.Core.Lyrics;

namespace LyricsLab.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        [ObservableProperty]
        private string audioFilePath = string.Empty;

        [ObservableProperty]
        private string lyricsText = string.Empty;

        [ObservableProperty]
        private TimeSpan totalAudioDuration;

        [ObservableProperty]
        private TimeSpan currentPosition;

        [ObservableProperty]
        private bool isPlaying;

        [ObservableProperty]
        private string activeLyricText = string.Empty;

        [ObservableProperty]
        private double currentPositionSeconds;

        [ObservableProperty]
        private double totalAudioDurationSeconds;

        [ObservableProperty]
        private string statusText = "Ready.";

        [ObservableProperty]
        private System.Collections.ObjectModel.ObservableCollection<string> availableEngines = new(new[] { "Auto (Oracle)", "WhisperX", "Whisper", "Vosk" });

        [ObservableProperty]
        private string selectedEngine = "Auto (Oracle)";

        [ObservableProperty]
        private System.Collections.ObjectModel.ObservableCollection<string> availableModels = new(new[] { "tiny", "base", "small", "medium", "large-v3" });

        [ObservableProperty]
        private string selectedModel = "small";

        private NAudio.Wave.IWavePlayer? _player;
        private NAudio.Wave.AudioFileReader? _audioFile;

        public MainViewModel()
        {
            System.Windows.Media.CompositionTarget.Rendering += OnRendering;
        }

        public void Dispose()
        {
            System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
            _player?.Dispose();
            _audioFile?.Dispose();
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!IsPlaying || _audioFile == null) return;

            CurrentPosition = _audioFile.CurrentTime;
            CurrentPositionSeconds = CurrentPosition.TotalSeconds;
            
            TimeSpan visualPosition = CurrentPosition;
            
            string currentLineText = "";
            LyricLineViewModel? activeLineToHighlight = null;
            
            foreach (var line in Lines)
            {
                bool shouldBeActive = (visualPosition >= line.StartTime && CurrentPosition <= line.EndTime);
                
                if (line.IsActive != shouldBeActive)
                {
                    line.IsActive = shouldBeActive;
                }

                if (shouldBeActive)
                {
                    activeLineToHighlight = line;
                }
            }

            if (activeLineToHighlight != null)
            {
                currentLineText = activeLineToHighlight.Text;
            }

            ActiveLyricText = currentLineText;
        }

        public Lab.UI.ViewModels.TerminalViewModel Terminal { get; } = new();

        public System.Collections.ObjectModel.ObservableCollection<LyricLineViewModel> Lines { get; } = new();

        [RelayCommand]
        private void PickLyrics()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "XML Lyrics (*.xml)|*.xml|Text Files (*.txt)|*.txt|All Files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    LyricsText = System.IO.File.ReadAllText(dialog.FileName);
                    StatusText = $"Loaded lyrics from {System.IO.Path.GetFileName(dialog.FileName)}";
                }
                catch (System.Exception ex)
                {
                    StatusText = $"Error loading lyrics: {ex.Message}";
                }
            }
        }

        private System.Threading.CancellationTokenSource? _analysisCts;

        [RelayCommand]
        private void CancelAnalysis()
        {
            _analysisCts?.Cancel();
            _analysisCts = null;
            StatusText = "Cancelled.";
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task AnalyzeAudioAsync()
        {
            if (string.IsNullOrWhiteSpace(LyricsText) || string.IsNullOrWhiteSpace(AudioFilePath))
            {
                StatusText = "Please select an audio file and paste lyrics first.";
                return;
            }

            _analysisCts?.Cancel();
            _analysisCts = new System.Threading.CancellationTokenSource();
            var ct = _analysisCts.Token;

            StatusText = "Analyzing audio duration with ffprobe...";
            var locator = new Lab.Core.Tools.ToolLocator();
            var ffprobePath = locator.Locate("ffprobe");
            
            if (ffprobePath == null)
            {
                StatusText = "Error: ffprobe.exe not found. Please install ffmpeg tools.";
                return;
            }

            var runner = new Lab.Core.Processes.ProcessRunner();
            var probeService = new LyricsLab.Core.Media.FfprobeService(runner, ffprobePath);
            var duration = await probeService.GetDurationAsync(AudioFilePath, ct);

            if (duration == null || duration.Value.TotalSeconds <= 0)
            {
                StatusText = "Error: Could not extract duration from audio file.";
                return;
            }

            TotalAudioDuration = duration.Value;
            
            var tools = locator;
            System.Collections.Generic.IReadOnlyList<LyricsLab.Core.Lyrics.TimedWord> words = new System.Collections.Generic.List<LyricsLab.Core.Lyrics.TimedWord>();
            try
            {
                if (SelectedEngine == "Auto (Oracle)")
                {
                    StatusText = "Aligning audio with Oracle Engine (Whisper + Vosk parallel)...";
                    var oracleService = new LyricsLab.Core.Lyrics.OracleTranscriberService(tools);
                    words = await oracleService.TranscribeOracleAsync(AudioFilePath, LyricsText, SelectedModel, line => 
                    {
                        Terminal.Append(line);
                    }, ct);
                }
                else if (SelectedEngine == "WhisperX")
                {
                    StatusText = $"Aligning audio with WhisperX ({SelectedModel})...";
                    var whisperx = new LyricsLab.Core.Lyrics.WhisperXTranscriberService(tools, runner);
                    words = await whisperx.TranscribeAsync(AudioFilePath, LyricsText, "auto", true, SelectedModel, line => Terminal.Append(line), ct);
                }
                else if (SelectedEngine == "Whisper")
                {
                    StatusText = $"Aligning audio with Whisper ({SelectedModel})...";
                    var whisper = new LyricsLab.Core.Lyrics.WhisperTranscriberService(tools, runner);
                    var modelFile = whisper.FindModel(SelectedModel) ?? whisper.FindModel() ?? throw new System.InvalidOperationException("Whisper model not found. Install via Setup.");
                    words = await whisper.TranscribeAsync(AudioFilePath, modelFile, "auto", LyricsText, line => Terminal.Append(line), ct);
                }
                else if (SelectedEngine == "Vosk")
                {
                    StatusText = "Aligning audio with Vosk...";
                    var vosk = new LyricsLab.Core.Lyrics.VoskTranscriberService(tools, runner);
                    var modelDir = vosk.FindModel() ?? throw new System.InvalidOperationException("Vosk model not found. Install via Setup.");
                    words = await vosk.TranscribeAsync(AudioFilePath, modelDir, LyricsText, line => Terminal.Append(line), ct);
                }
            }
            catch (System.OperationCanceledException)
            {
                StatusText = "Alignment Cancelled.";
                return;
            }
            catch (System.InvalidOperationException ex)
            {
                StatusText = $"Alignment Failed: {ex.Message} (Try 'Setup Dependencies')";
                Terminal.Append($"[error] {ex}");
                return;
            }
            catch (System.Exception ex)
            {
                StatusText = $"Alignment Failed: {ex.Message}";
                Terminal.Append($"[error] {ex}");
                return;
            }
            finally
            {
                _analysisCts = null;
            }

            LyricsLab.Core.Lyrics.SongLyrics songLyrics;
            try
            {
                songLyrics = LyricsLab.Core.Lyrics.LyricsXmlParser.Parse(LyricsText);
            }
            catch
            {
                StatusText = "Error: Lyrics must be valid XML. Use 'Prepare Lyrics' to structure them.";
                return;
            }

            var alignedLines = LyricsLab.Core.Lyrics.LyricsAligner.Align(songLyrics, words, new LyricsLab.Core.Lyrics.AlignerOptions { TotalDuration = TotalAudioDuration });
            
            Lines.Clear();
            foreach (var line in alignedLines)
            {
                Lines.Add(new LyricLineViewModel
                {
                    Text = line.Text,
                    StartTime = line.Start,
                    EndTime = line.End,
                    IsAnchor = line.Confidence == LyricsLab.Core.Lyrics.AlignmentConfidence.High // Suggest high confidence as anchors initially, or just default false
                });
            }
            
            StatusText = $"Alignment complete. Matched {words.Count} words.";
        }

        [RelayCommand]
        private void PickAudio()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio Files|*.mp3;*.wav;*.flac;*.m4a|All Files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                AudioFilePath = dialog.FileName;
                try 
                { 
                    _player?.Dispose();
                    _audioFile?.Dispose();
                    
                    _audioFile = new NAudio.Wave.AudioFileReader(AudioFilePath);
                    _player = new NAudio.Wave.WaveOutEvent();
                    _player.Init(_audioFile);
                    
                    TotalAudioDuration = _audioFile.TotalTime;
                    TotalAudioDurationSeconds = TotalAudioDuration.TotalSeconds;
                    
                    _player.PlaybackStopped += (s, e) => { IsPlaying = false; };
                } 
                catch (System.Exception ex)
                {
                    StatusText = $"Error loading audio: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        private void PlayPause()
        {
            if (string.IsNullOrWhiteSpace(AudioFilePath)) return;

            if (IsPlaying)
            {
                _player?.Pause();
                IsPlaying = false;
            }
            else
            {
                _player?.Play();
                IsPlaying = true;
            }
        }

        partial void OnCurrentPositionSecondsChanged(double value)
        {
            if (_audioFile != null && Math.Abs(_audioFile.CurrentTime.TotalSeconds - value) > 0.5)
            {
                _audioFile.CurrentTime = TimeSpan.FromSeconds(value);
            }
        }

        [RelayCommand]
        private void PrepareLyrics()
        {
            var tools = new Lab.Core.Tools.ToolLocator();
            var oracleService = new LyricsLab.Core.Lyrics.OracleTranscriberService(tools);
            var runner = new Lab.Core.Processes.ProcessRunner();
            var vadDetector = new LyricsLab.Core.Lyrics.InstrumentalDetectorService(tools, runner);

            var editor = new UI.Views.LyricsEditorWindow(LyricsText, AudioFilePath, oracleService, vadDetector);
            if (editor.ShowDialog() == true)
            {
                LyricsText = editor.Result ?? "";
                StatusText = "Lyrics prepared successfully.";
            }
        }

        [RelayCommand]
        private void SetupDependencies()
        {
            var setup = new UI.Views.SetupWindow();
            setup.ShowDialog();
        }

        [RelayCommand]
        private void Export()
        {
            if (Lines.Count == 0) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "SubRip (*.srt)|*.srt|Lyric (*.lrc)|*.lrc|WebVTT (*.vtt)|*.vtt|Advanced SubStation Alpha (*.ass)|*.ass|YouTube SBV (*.sbv)|*.sbv|MicroDVD (*.sub)|*.sub|JSON (*.json)|*.json",
                DefaultExt = ".srt"
            };

            if (dialog.ShowDialog() == true)
            {
                var alignedLines = new System.Collections.Generic.List<AlignedLine>();
                foreach (var l in Lines)
                {
                    alignedLines.Add(new AlignedLine(l.Text, l.StartTime, l.EndTime, 1.0, LyricsLab.Core.Lyrics.AlignmentConfidence.High, "verse"));
                }

                string content = "";
                var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
                switch (ext)
                {
                    case ".srt": content = LyricsLab.Core.Lyrics.SubtitleWriter.ToSrt(alignedLines); break;
                    case ".lrc": content = LyricsLab.Core.Lyrics.SubtitleWriter.ToLrc(alignedLines); break;
                    case ".vtt": content = LyricsLab.Core.Lyrics.SubtitleWriter.ToVtt(alignedLines); break;
                    case ".ass": content = LyricsLab.Core.Lyrics.SubtitleWriter.ToAss(alignedLines); break;
                    case ".sbv": content = LyricsLab.Core.Lyrics.SubtitleWriter.ToSubViewer(alignedLines); break;
                    case ".sub": content = LyricsLab.Core.Lyrics.SubtitleWriter.ToMicroDvd(alignedLines); break;
                    case ".json": content = LyricsLab.Core.Lyrics.SubtitleWriter.ToJson(alignedLines); break;
                }

                if (!string.IsNullOrEmpty(content))
                {
                    System.IO.File.WriteAllText(dialog.FileName, content, System.Text.Encoding.UTF8);
                }
            }
        }

        [RelayCommand]
        private void SaveXml()
        {
            if (string.IsNullOrWhiteSpace(LyricsText)) return;

            try
            {
                var originalLyrics = LyricsXmlParser.Parse(LyricsText);
                
                // Re-build sections with the updated anchors/distances from Lines collection
                var newSections = new System.Collections.Generic.List<LyricSection>();
                int lineIndex = 0;

                foreach (var section in originalLyrics.Sections)
                {
                    if (section.IsInstrumental || section.IsReference)
                    {
                        newSections.Add(section);
                        continue;
                    }

                    var newLines = new System.Collections.Generic.List<LyricLineData>();
                    foreach (var originalLine in section.Lines)
                    {
                        if (lineIndex < Lines.Count)
                        {
                            var vmLine = Lines[lineIndex];
                            newLines.Add(new LyricLineData(originalLine.Text, vmLine.IsAnchor, vmLine.DistanceNext, vmLine.StartTime, vmLine.EndTime));
                            lineIndex++;
                        }
                        else
                        {
                            newLines.Add(originalLine);
                        }
                    }
                    newSections.Add(section with { Lines = newLines });
                }

                var updatedLyrics = originalLyrics with { Sections = newSections };
                
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "XML Lyrics (*.xml)|*.xml",
                    DefaultExt = ".xml"
                };

                if (dialog.ShowDialog() == true)
                {
                    var xml = LyricsXmlWriter.Write(updatedLyrics);
                    System.IO.File.WriteAllText(dialog.FileName, xml);
                    StatusText = "Anchored XML saved successfully.";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Failed to save XML: {ex.Message}";
            }
        }
    }
}
