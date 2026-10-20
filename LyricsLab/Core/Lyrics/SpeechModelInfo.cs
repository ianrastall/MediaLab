namespace LyricsLab.Core.Lyrics;

/// <summary>Identifies which speech-recognition engine a model belongs to.</summary>
public enum SpeechEngine { Whisper, Vosk }

/// <summary>Metadata for a downloadable speech-recognition model file or directory.</summary>
public sealed record SpeechModelInfo(
    SpeechEngine Engine,
    string DisplayName,
    string FileName,    // whisper: ggml-*.bin   vosk: vosk-model-*.zip (dir name after extract)
    long SizeMb,
    string Description,
    bool IsRecommended = false,
    bool IsWarning = false);
