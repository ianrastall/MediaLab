using System.IO;
using System.Text.Json;
using Lab.Core.Processes;
using Lab.Core.Tools;
using Vosk;

namespace LyricsLab.Core.Lyrics;

/// <summary>
/// Transcribes audio to timed words using the Vosk Kaldi-based speech recognizer.
/// Pipeline: ffmpeg → 16 kHz mono WAV → VoskRecognizer → <see cref="TimedWord"/>s.
/// Vosk uses a completely different acoustic model architecture from Whisper (CTC/Kaldi vs
/// seq2seq transformer), so it provides a genuinely independent second opinion on timing.
/// No separate binary needed — the Vosk native library ships via the NuGet package.
/// </summary>
public sealed class VoskTranscriberService
{
    private readonly ToolLocator _tools;
    private readonly IProcessRunner _runner;

    public VoskTranscriberService(ToolLocator tools, IProcessRunner? runner = null)
    {
        _tools = tools;
        _runner = runner ?? new ProcessRunner();
        Vosk.Vosk.SetLogLevel(-1); // suppress Kaldi verbose output
    }

    /// <summary>
    /// Returns the directory of the best available Vosk model, or null if none are installed.
    /// </summary>
    public string? FindModel(string? installDir = null) =>
        WhisperInstallerService.FindVoskModelDir(installDir);

    /// <summary>
    /// Converts <paramref name="audioPath"/> to 16 kHz mono WAV via ffmpeg, then runs Vosk
    /// and returns word-level timed words.
    /// </summary>
    /// <param name="audioPath">Source audio (any ffmpeg-readable format).</param>
    /// <param name="modelDirPath">
    /// Path to a Vosk model directory. When null, <see cref="FindModel"/> is called.
    /// </param>
    /// <param name="grammarText">
    /// Optional known lyrics. When supplied AND the model is grammar-capable (a "small" Vosk model),
    /// Vosk is constrained to only the words present here — turning it from a blind transcriber
    /// (which flails on sung, instrumented vocals) into a near-forced-aligner that just decides
    /// <em>when</em> the known words occur. Out-of-grammar audio falls through to <c>[unk]</c>.
    /// Ignored on large models, which don't support dynamic grammar.
    /// </param>
    /// <param name="onLogLine">Receives progress lines for display in the UI log.</param>
    /// <param name="cancellationToken">Cancels the ffmpeg conversion and Vosk inference.</param>
    public async Task<IReadOnlyList<TimedWord>> TranscribeAsync(
        string audioPath,
        string? modelDirPath = null,
        string? grammarText = null,
        Action<string>? onLogLine = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);

        var ffmpeg = _tools.Locate("ffmpeg")
            ?? throw new InvalidOperationException("ffmpeg not found. Check tool paths in Settings.");

        modelDirPath ??= FindModel()
            ?? throw new InvalidOperationException(
                "No Vosk model directory found. Install a model via Setup.");

        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}", audioPath);
        if (!Directory.Exists(modelDirPath))
            throw new DirectoryNotFoundException($"Vosk model directory not found: {modelDirPath}");

        // Grammar only works on the small models; the large one silently ignores it. Build the
        // grammar (and warn) only when we have a model that can actually use it.
        string? grammar = null;
        if (!string.IsNullOrWhiteSpace(grammarText))
        {
            if (IsGrammarCapable(modelDirPath))
            {
                grammar = BuildGrammar(grammarText);
                onLogLine?.Invoke("[vosk] Constraining recognition to the supplied lyrics (grammar mode).");
            }
            else
            {
                onLogLine?.Invoke(
                    "[vosk] Lyrics provided but the installed model doesn't support grammar — " +
                    "install 'vosk-model-small-en-us-0.15' via Setup for lyric-constrained alignment.");
            }
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"ml_vosk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var wavPath = Path.Combine(tempDir, "audio.wav");

        try
        {
            onLogLine?.Invoke("[vosk] Converting audio to 16 kHz mono WAV (speech-shaped)…");
            var ffResult = await _runner.RunAsync(
                ffmpeg,
                ["-i", audioPath,
                 "-ar", "16000", "-ac", "1",
                 "-af", VoskAudioFilter,            // compress + de-rumble + boost consonant band
                 "-c:a", "pcm_s16le", wavPath, "-y"],
                onErrorLine: onLogLine,
                captureOutput: false,
                cancellationToken: cancellationToken);

            if (ffResult.Canceled)
                throw new OperationCanceledException(cancellationToken);
            if (ffResult.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg conversion failed (exit {ffResult.ExitCode}).");

            onLogLine?.Invoke($"[vosk] Loading model: {Path.GetFileName(modelDirPath)}");

            // Model load and inference run on a thread pool thread so we don't block the UI.
            var words = await Task.Run(() =>
                RunVosk(modelDirPath, wavPath, grammar, onLogLine, cancellationToken),
                cancellationToken);

            onLogLine?.Invoke($"[vosk] Done — {words.Count} words.");
            return words;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { onLogLine?.Invoke($"[vosk] Warning: could not delete temp dir: {ex.Message}"); }
        }
    }

    /// <summary>
    /// ffmpeg audio-filter chain applied before Vosk only. Vocals in a mix don't match the clean,
    /// close-mic speech Vosk was trained on, so we nudge the signal toward that distribution:
    /// high-pass away sub-bass rumble/instrument bleed, compress the wide singing dynamics, and
    /// boost the 2–4 kHz consonant band where intelligibility lives (often masked by guitars).
    /// Duration is unchanged, so timestamps still map onto the original audio.
    /// </summary>
    private const string VoskAudioFilter =
        "highpass=f=90," +
        "acompressor=threshold=-20dB:ratio=4:attack=5:release=60:makeup=3," +
        "equalizer=f=3000:width_type=q:width=1.4:g=6";

    private static bool IsGrammarCapable(string modelDirPath) =>
        Path.GetFileName(modelDirPath).Contains("small", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds a Vosk grammar (JSON array of allowed words) from the lyrics. Word-level rather than
    /// phrase-level so repeated/rearranged sung lines still match. <c>[unk]</c> lets genuinely
    /// out-of-grammar audio (samples, ad-libs) decode as unknown instead of forcing a wrong word.
    /// </summary>
    private static string BuildGrammar(string lyricsText)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in lyricsText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // Keep letters and inner apostrophes (don't, runnin'); lowercase to match model vocab.
            var norm = new string(raw.ToLowerInvariant()
                .Where(c => char.IsLetter(c) || c == '\'').ToArray()).Trim('\'');
            if (norm.Length > 0)
                words.Add(norm);
        }

        var phrases = new List<string>(words) { "[unk]" };
        return JsonSerializer.Serialize(phrases);
    }

    private static List<TimedWord> RunVosk(
        string modelDir, string wavPath, string? grammar,
        Action<string>? onLog, CancellationToken ct)
    {
        using var model = new Model(modelDir);

        // Grammar-constrained recognizer can throw if a lyric word isn't in the model vocabulary;
        // fall back to unconstrained recognition rather than failing the whole run.
        VoskRecognizer rec;
        try
        {
            rec = grammar is not null
                ? new VoskRecognizer(model, 16000.0f, grammar)
                : new VoskRecognizer(model, 16000.0f);
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"[vosk] Grammar rejected ({ex.Message}); falling back to open recognition.");
            rec = new VoskRecognizer(model, 16000.0f);
        }

        using (rec)
        {
            rec.SetWords(true);

            var results = new List<string>();

            using var fs = File.OpenRead(wavPath);
            SkipWavHeader(fs);

            var buf = new byte[8192];
            int read;
            while (!ct.IsCancellationRequested && (read = fs.Read(buf, 0, buf.Length)) > 0)
            {
                if (rec.AcceptWaveform(buf, read))
                    results.Add(rec.Result());
            }
            results.Add(rec.FinalResult());

            ct.ThrowIfCancellationRequested();

            var words = new List<TimedWord>();
            foreach (var json in results)
                ParseVoskResult(json, words);

            return words;
        }
    }

    private static void ParseVoskResult(string json, List<TimedWord> words)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var result)
                || result.ValueKind != JsonValueKind.Array)
                return;

            foreach (var entry in result.EnumerateArray())
            {
                var text = entry.TryGetProperty("word", out var w) ? w.GetString() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;

                // Vosk timestamps are in seconds; convert to TimeSpan.
                var start = entry.TryGetProperty("start", out var s) && s.TryGetDouble(out var sv)
                    ? TimeSpan.FromSeconds(sv) : TimeSpan.Zero;
                var end   = entry.TryGetProperty("end",   out var e) && e.TryGetDouble(out var ev)
                    ? TimeSpan.FromSeconds(ev) : start;

                // Per-word confidence in [0,1]; defaults to 1.0 if Vosk omits it.
                var conf = entry.TryGetProperty("conf", out var c) && c.TryGetDouble(out var cv)
                    ? cv : 1.0;

                words.Add(new TimedWord(text.Trim(), start, end, conf));
            }
        }
        catch (JsonException)
        {
            // Partial / malformed segment — skip.
        }
    }

    /// <summary>
    /// Advances <paramref name="stream"/> past the RIFF WAV header to the start of PCM data.
    /// Handles the standard 44-byte header; falls back gracefully for non-standard headers.
    /// </summary>
    private static void SkipWavHeader(Stream stream)
    {
        // RIFF chunk: "RIFF" (4) + size (4) + "WAVE" (4) = 12 bytes
        var header = new byte[12];
        if (stream.Read(header, 0, 12) < 12) return;
        if (header[0] != 'R' || header[1] != 'I' || header[2] != 'F' || header[3] != 'F') return;

        // Scan sub-chunks until we find "data"
        var chunkId = new byte[4];
        var chunkSz = new byte[4];
        while (stream.Position < stream.Length - 8)
        {
            if (stream.Read(chunkId, 0, 4) < 4) break;
            if (stream.Read(chunkSz, 0, 4) < 4) break;
            var size = BitConverter.ToUInt32(chunkSz, 0);
            if (chunkId[0] == 'd' && chunkId[1] == 'a' && chunkId[2] == 't' && chunkId[3] == 'a')
                return; // stream is now positioned at the first PCM byte
            stream.Seek(size, SeekOrigin.Current);
        }
    }
}
