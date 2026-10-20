using System.IO;
using System.Text.Json;
using Lab.Core.Processes;
using Lab.Core.Tools;

namespace LyricsLab.Core.Lyrics;

/// <summary>
/// Transcribes audio to timed words using a local whisper-cli installation.
/// Pipeline: ffmpeg → 16 kHz mono WAV → whisper-cli -oj -ml 1 -sow → JSON → <see cref="TimedWord"/>s.
/// </summary>
public sealed class WhisperTranscriberService
{
    private readonly ToolLocator _tools;
    private readonly IProcessRunner _runner;

    public WhisperTranscriberService(ToolLocator tools, IProcessRunner? runner = null)
    {
        _tools = tools;
        _runner = runner ?? new ProcessRunner();
    }

    /// <summary>
    /// Finds the best available ggml model in the whisper-cli directory for unattended use.
    /// Favors the reliable sweet spot (small > base > medium > tiny) and deliberately ranks the
    /// largest model last: <c>large-v3</c> is the most accurate but is slow and memory-hungry and
    /// "chokes" on modest machines, so it is never auto-selected over a lighter model that is
    /// present. (A future model picker can let power users force large explicitly.)
    /// Returns null if whisper-cli cannot be located or the directory has no ggml-*.bin files.
    /// </summary>
    public string? FindModel(string? modelName = null)
    {
        var whisperExe = _tools.Locate("whisper-cli");
        if (whisperExe is null)
            return null;

        var whisperDir = Path.GetDirectoryName(Path.GetFullPath(whisperExe));
        if (whisperDir is null || !Directory.Exists(whisperDir))
            return null;

        if (!string.IsNullOrWhiteSpace(modelName))
        {
            var exact = Path.Combine(whisperDir, "ggml-" + modelName + ".bin");
            if (File.Exists(exact)) return exact;
            var exactEn = Path.Combine(whisperDir, "ggml-" + modelName + ".en.bin");
            if (File.Exists(exactEn)) return exactEn;
        }

        return Directory.GetFiles(whisperDir, "ggml-*.bin")
            .OrderByDescending(ModelScore)
            .ThenByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>
    /// Converts <paramref name="audioPath"/> to 16 kHz mono WAV, runs whisper-cli, and returns
    /// timed words parsed from the resulting JSON.
    /// </summary>
    /// <param name="audioPath">Source audio (any ffmpeg-readable format).</param>
    /// <param name="modelPath">
    /// Absolute path to a ggml-*.bin model. When null, <see cref="FindModel"/> is called
    /// to auto-select the best available model in the whisper-cli directory.
    /// </param>
    /// <param name="language">
    /// BCP-47 language code (e.g. "en", "fr") or "auto" to let Whisper detect it.
    /// Forcing a language prevents the decoder from hallucinating in the wrong language.
    /// </param>
    /// <param name="initialPrompt">
    /// Optional text passed to Whisper as context before decoding begins (max ~500 chars).
    /// Passing actual song lyrics here strongly biases the language-model component toward
    /// the correct vocabulary and dramatically reduces hallucinations on difficult audio.
    /// </param>
    /// <param name="onLogLine">Receives ffmpeg/whisper output lines for display in the UI log.</param>
    /// <param name="cancellationToken">Cancelling kills both child processes and cleans up temp files.</param>
    public async Task<IReadOnlyList<TimedWord>> TranscribeAsync(
        string audioPath,
        string? modelPath = null,
        string language = "auto",
        string? initialPrompt = null,
        Action<string>? onLogLine = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);

        var ffmpeg = _tools.Locate("ffmpeg")
            ?? throw new InvalidOperationException("ffmpeg not found. Check tool paths in Settings.");
        var whisperCli = _tools.Locate("whisper-cli")
            ?? throw new InvalidOperationException("whisper-cli not found. Check tool paths in Settings.");

        modelPath ??= FindModel()
            ?? throw new InvalidOperationException(
                "No ggml-*.bin model file found alongside whisper-cli. " +
                "Download a model (e.g. ggml-large-v3.bin) into the whisper directory.");

        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}", audioPath);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Whisper model not found: {modelPath}", modelPath);

        var tempDir = Path.Combine(Path.GetTempPath(), $"ml_whisper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var wavPath = Path.Combine(tempDir, "audio.wav");
        var jsonBase = Path.Combine(tempDir, "out");
        var jsonPath = jsonBase + ".json";

        try
        {
            // Convert to 16 kHz mono WAV — the format whisper is trained on.
            onLogLine?.Invoke("[whisper] Converting audio to 16 kHz mono WAV...");
            var ffmpegResult = await _runner.RunAsync(
                ffmpeg,
                ["-i", audioPath, "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wavPath, "-y"],
                onErrorLine: onLogLine,
                captureOutput: false,
                cancellationToken: cancellationToken);

            if (ffmpegResult.Canceled)
                throw new OperationCanceledException(cancellationToken);
            if (ffmpegResult.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg conversion failed (exit {ffmpegResult.ExitCode}).");

            // Transcribe with whisper-cli.
            // -ml 1 -sow: one token per entry, split on word → finest-grain timestamps.
            // -oj: JSON output. -of: output path base (whisper appends .json).
            // -np: suppress internal progress prints; errors still surface via stderr callback.
            // --temperature 0: start with greedy decoding but allow normal temperature fallback
            //   (0.2 → 0.4 → … → 1.0) so whisper can retry segments where greedy confidence
            //   is low. Songs with background music frequently trigger fallback; suppressing it
            //   (--no-fallback / --temperature-inc 0) causes all timestamps to collapse to ~0:00.
            //   Hallucination loops produced by the fallback are caught post-hoc by FilterHallucinationLoops.
            // --language: prevents mid-song language switches that produce nonsense.
            // --prompt: seeds the LM context with actual lyric vocabulary — the single
            //   most effective anti-hallucination lever when you already have the correct text.
            onLogLine?.Invoke($"[whisper] Transcribing with model: {Path.GetFileName(modelPath)}");
            var args = new List<string>
            {
                "-m", modelPath, "-f", wavPath,
                "-oj", "-ml", "1", "-sow", "-of", jsonBase, "-np",
                "--temperature", "0",
            };
            if (!string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
                args.AddRange(["--language", language]);
            if (!string.IsNullOrWhiteSpace(initialPrompt))
            {
                // Limit to 500 chars (~130 tokens) to stay within the prompt context budget.
                var prompt = initialPrompt.Length > 500 ? initialPrompt[..500] : initialPrompt;
                args.AddRange(["--prompt", prompt]);
                onLogLine?.Invoke($"[whisper] Using lyrics hint ({prompt.Length} chars).");
            }

            var whisperResult = await _runner.RunAsync(
                whisperCli,
                [.. args],
                onOutputLine: onLogLine,
                onErrorLine: onLogLine,
                captureOutput: false,
                cancellationToken: cancellationToken);

            if (whisperResult.Canceled)
                throw new OperationCanceledException(cancellationToken);
            if (whisperResult.ExitCode != 0)
                throw new InvalidOperationException($"whisper-cli failed (exit {whisperResult.ExitCode}).");

            if (!File.Exists(jsonPath))
                throw new InvalidOperationException(
                    $"whisper-cli did not produce {jsonPath}. " +
                    "Ensure whisper-cli supports -oj output.");

            var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
            IReadOnlyList<TimedWord> words;
            try { words = WhisperCppJsonParser.Parse(json); }
            catch (JsonException ex)
            { throw new InvalidOperationException($"whisper-cli produced malformed JSON: {ex.Message}", ex); }

            // Sanity-check: if most of the transcript words land within the first second,
            // whisper compressed its timestamps (common at temperature=0 on music).
            if (words.Count > 10)
            {
                var nearZero = words.Count(w => w.Start.TotalSeconds < 1.0);
                if (nearZero > words.Count / 2)
                    onLogLine?.Invoke(
                        $"[whisper] Warning: {nearZero}/{words.Count} words have timestamps < 1 s — " +
                        "alignment quality will be poor. Try a different model or audio segment.");
            }

            var rawCount = words.Count;
            words = FilterHallucinationLoops(words);
            if (words.Count < rawCount)
                onLogLine?.Invoke(
                    $"[whisper] Removed {rawCount - words.Count} hallucination-loop word(s); " +
                    $"kept {words.Count} clean words spanning the track.");

            var afterLoops = words.Count;
            words = FilterTrailingHallucinations(words);
            if (words.Count < afterLoops)
                onLogLine?.Invoke(
                    $"[whisper] Trailing hallucination stripped ({afterLoops - words.Count} word(s) removed).");

            onLogLine?.Invoke($"[whisper] Done — {words.Count} words transcribed.");
            return words;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { onLogLine?.Invoke($"[whisper] Warning: could not delete temp dir {tempDir}: {ex.Message}"); }
        }
    }

    // Phrases Whisper appends to silence or near-silence at the end of audio clips.
    // Normalised (letters only, lower-case) and ordered longest-first to avoid partial matches
    // shadowing longer phrases (e.g. "thanks" before "thanks for watching").
    private static readonly string[][] s_trailingPhrases =
    [
        ["thanks", "for", "watching"],
        ["thanks", "for", "listening"],
        ["see", "you", "next", "time"],
        ["please", "subscribe"],
        ["thank", "you"],
        ["thanks"],
        ["subscribe"],
    ];

    /// <summary>
    /// Strips well-known trailing phrases Whisper appends to silence at the end of audio
    /// (e.g. "Thank you", "Thanks for watching"). Matches are case-insensitive, letters only.
    /// </summary>
    private static IReadOnlyList<TimedWord> FilterTrailingHallucinations(IReadOnlyList<TimedWord> words)
    {
        if (words.Count == 0) return words;
        foreach (var phrase in s_trailingPhrases)
        {
            if (words.Count < phrase.Length) continue;
            var matched = true;
            for (var j = 0; j < phrase.Length; j++)
            {
                if (!string.Equals(
                    NormalizeLetters(words[words.Count - phrase.Length + j].Text),
                    phrase[j], StringComparison.Ordinal))
                {
                    matched = false;
                    break;
                }
            }
            if (matched)
                return words.Take(words.Count - phrase.Length).ToList();
        }
        return words;
    }

    private static string NormalizeLetters(string text) =>
        new string(text.ToLowerInvariant().Where(char.IsLetter).ToArray());

    /// <summary>
    /// Removes Whisper's hallucination-loop artifacts while preserving clean audio that resumes
    /// afterward. On difficult audio (music, distortion) the greedy decoder periodically stalls and
    /// repeats a phrase with frozen timestamps, then recovers and continues transcribing correctly —
    /// often several times across one song. The frozen stretches have a definitive signature: words
    /// with zero/near-zero duration (start == end) or a start time that jumps backward. We drop just
    /// those words and keep everything else, rather than truncating the whole tail at the first
    /// artifact (which would discard minutes of perfectly good transcription that follow the loop).
    /// </summary>
    private static IReadOnlyList<TimedWord> FilterHallucinationLoops(IReadOnlyList<TimedWord> words)
    {
        var kept = new List<TimedWord>(words.Count);
        var lastStart = TimeSpan.MinValue;
        foreach (var w in words)
        {
            // Frozen clock: zero/near-zero duration is the hallmark of a stalled loop.
            if ((w.End - w.Start).TotalMilliseconds < 20)
                continue;

            // Time travelled backward: the decoder rewound into a repeat.
            if (w.Start < lastStart)
                continue;

            kept.Add(w);
            lastStart = w.Start;
        }
        return kept;
    }

    // Auto-selection preference (best → worst): small > base > medium > tiny > large.
    // 'large' is ranked last on purpose — it is accurate but heavy enough to choke modest machines,
    // so it is only ever used when it is the sole model present.
    private static int ModelScore(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Contains("small", StringComparison.OrdinalIgnoreCase)) return 5;
        if (name.Contains("base", StringComparison.OrdinalIgnoreCase)) return 4;
        if (name.Contains("medium", StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Contains("tiny", StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.Contains("large", StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }
}
