namespace LyricsLab.Core.Lyrics;

/// <summary>
/// One transcribed word with its timing — the normalized unit the aligner works against,
/// independent of which transcriber produced it. A thin per-transcriber adapter (e.g. whisper.cpp
/// JSON) converts raw output into a sequence of these.
/// </summary>
/// <param name="Confidence">
/// Per-word confidence in [0,1]. Vosk reports this directly; Whisper words default to 1.0
/// (we treat the primary engine as trusted). Used to gate which secondary-engine words are
/// merged in — a low-confidence Vosk word is dropped rather than allowed to pollute the result.
/// </param>
public readonly record struct TimedWord(string Text, TimeSpan Start, TimeSpan End, double Confidence = 1.0);
