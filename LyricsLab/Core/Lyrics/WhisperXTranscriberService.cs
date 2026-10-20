using System.IO;
using System.Text;
using System.Text.Json;
using Lab.Core.Processes;
using Lab.Core.Tools;

namespace LyricsLab.Core.Lyrics;

/// <summary>
/// Transcribes/aligns audio using whisperX running in a dedicated Python venv. whisperX pairs
/// faster-whisper with wav2vec2 forced alignment, giving far more accurate word-level timestamps
/// than whisper.cpp's native token times.
///
/// <para>Two modes, chosen by whether lyrics are supplied:
/// <list type="bullet">
///   <item><b>Transcribe + align</b>: faster-whisper transcribes, then wav2vec2 refines
///   the word timings. If lyrics are supplied, they are passed to the transcriber as an
///   initial prompt to bias the model towards the correct vocabulary and reduce hallucinations.
///   (We no longer use WhisperX's native 'forced-alignment' mode because it forces all text
///   into a single segment spanning the entire audio, completely destroying VAD silences.)</item>
/// </list></para>
///
/// <para>Audio is pre-converted to 16 kHz mono WAV by our own ffmpeg and read in Python via the
/// stdlib <c>wave</c> module, so the venv doesn't need ffmpeg on its PATH.</para>
/// </summary>
public sealed class WhisperXTranscriberService
{
    private readonly ToolLocator _tools;
    private readonly IProcessRunner _runner;
    private readonly string _installDir;

    public WhisperXTranscriberService(ToolLocator tools, IProcessRunner? runner = null, string? installDir = null)
    {
        _tools = tools;
        _runner = runner ?? new ProcessRunner();
        _installDir = installDir ?? WhisperInstallerService.ResolveWhisperXInstallDir();
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    public static string VenvDir(string installDir)    => Path.Combine(installDir, "whisperx-venv");
    public static string VenvPython(string installDir)  => Path.Combine(VenvDir(installDir), "Scripts", "python.exe");
    public static string VenvPip(string installDir)     => Path.Combine(VenvDir(installDir), "Scripts", "pip.exe");
    public static string DriverPath(string installDir)  => Path.Combine(installDir, "whisperx_driver.py");

    /// <summary>True when the venv python and the driver script are both present.</summary>
    public bool IsInstalled() =>
        File.Exists(VenvPython(_installDir)) && File.Exists(DriverPath(_installDir));

    // ── Transcription / alignment ───────────────────────────────────────────────

    /// <param name="audioPath">Source audio (any ffmpeg-readable format).</param>
    /// <param name="lyricsText">
    /// Known lyrics (plain text, one line per line). When non-empty, whisperX uses these as an initial
    /// prompt for the transcriber to bias its output, rather than forcing true forced-alignment.
    /// </param>
    /// <param name="language">BCP-47 code or "auto". Drives the wav2vec2 alignment model.</param>
    /// <param name="preferGpu">Request CUDA; the driver downgrades to CPU if torch can't see a GPU.</param>
    public async Task<IReadOnlyList<TimedWord>> TranscribeAsync(
        string audioPath,
        string? lyricsText = null,
        string language = "auto",
        bool preferGpu = true,
        string model = "small",
        Action<string>? onLogLine = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);

        if (!IsInstalled())
            throw new InvalidOperationException(
                "whisperX is not installed. Install it from the Setup window first.");

        var ffmpeg = _tools.Locate("ffmpeg")
            ?? throw new InvalidOperationException("ffmpeg not found. Check tool paths in Settings.");
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}", audioPath);

        var tempDir = Path.Combine(Path.GetTempPath(), $"ml_whisperx_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var wavPath  = Path.Combine(tempDir, "audio.wav");
        var jsonPath = Path.Combine(tempDir, "out.json");
        var lyricsPath = Path.Combine(tempDir, "lyrics.txt");

        try
        {
            onLogLine?.Invoke("[whisperx] Converting audio to 16 kHz mono WAV…");
            var ff = await _runner.RunAsync(
                ffmpeg,
                ["-i", audioPath, "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wavPath, "-y"],
                onErrorLine: onLogLine, captureOutput: false, cancellationToken: cancellationToken);

            if (ff.Canceled) throw new OperationCanceledException(cancellationToken);
            if (ff.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg conversion failed (exit {ff.ExitCode}).");

            var args = new List<string>
            {
                DriverPath(_installDir),
                "--audio", wavPath,
                "--output", jsonPath,
                "--device", preferGpu ? "cuda" : "cpu",
                "--compute-type", "int8",      // int8 works on CPU and avoids the GTX 16-series fp16 bug
                "--model", model,
                "--language", language,
            };

            var plainLyrics = ExtractPlainText(lyricsText);
            if (!string.IsNullOrWhiteSpace(plainLyrics))
            {
                await File.WriteAllTextAsync(lyricsPath, plainLyrics, cancellationToken);
                args.AddRange(["--lyrics", lyricsPath]);
                onLogLine?.Invoke("[whisperx] Transcribe-and-align mode (using supplied lyrics as hint).");
            }
            else
            {
                onLogLine?.Invoke("[whisperx] Transcribe-and-align mode (no lyrics supplied).");
            }

            var run = await _runner.RunAsync(
                VenvPython(_installDir),
                args,
                onOutputLine: onLogLine, onErrorLine: onLogLine,
                captureOutput: false, cancellationToken: cancellationToken);

            if (run.Canceled) throw new OperationCanceledException(cancellationToken);
            if (run.ExitCode != 0)
                throw new InvalidOperationException($"whisperX driver failed (exit {run.ExitCode}).");
            if (!File.Exists(jsonPath))
                throw new InvalidOperationException("whisperX produced no output JSON.");

            var json  = await File.ReadAllTextAsync(jsonPath, cancellationToken);
            var words = ParseWords(json);
            onLogLine?.Invoke($"[whisperx] Done — {words.Count} words.");
            return words;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex)
            { onLogLine?.Invoke($"[whisperx] Warning: could not delete temp dir: {ex.Message}"); }
        }
    }

    /// <summary>Flattens XML or plain-text lyrics into one word stream for forced alignment.</summary>
    private static string ExtractPlainText(string? lyricsText)
    {
        if (string.IsNullOrWhiteSpace(lyricsText))
            return string.Empty;

        if (lyricsText.TrimStart().StartsWith('<'))
        {
            var lyrics = LyricsXmlParser.Parse(lyricsText);
            return string.Join("\n", lyrics.VocalLines().Select(l => l.Data.Text));
        }

        return string.Join("\n", lyricsText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0));
    }

    private static IReadOnlyList<TimedWord> ParseWords(string json)
    {
        var result = new List<TimedWord>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("words", out var words) || words.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var w in words.EnumerateArray())
        {
            var text = w.TryGetProperty("text", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!w.TryGetProperty("start", out var s) || !s.TryGetDouble(out var start)) continue;
            if (!w.TryGetProperty("end", out var e) || !e.TryGetDouble(out var end)) continue;
            var score = w.TryGetProperty("score", out var sc) && sc.TryGetDouble(out var scv) ? scv : 1.0;

            result.Add(new TimedWord(
                text.Trim(),
                TimeSpan.FromSeconds(start),
                TimeSpan.FromSeconds(end),
                score));
        }
        return result;
    }

    // ── Driver script (written to disk at install time) ─────────────────────────

    /// <summary>
    /// The Python driver. Reads a 16 kHz mono PCM16 WAV via the stdlib (no ffmpeg dependency in the
    /// venv), runs whisperX in forced-alignment or transcribe-and-align mode, and writes a flat
    /// <c>{language, words:[{text,start,end,score}]}</c> JSON.
    /// </summary>
    public const string DriverScript = """
        #!/usr/bin/env python3
        # Auto-generated by LyricsLab. Drives whisperX for word-level alignment.
        import argparse, json, sys, wave
        import numpy as np

        SAMPLE_RATE = 16000

        def log(msg):
            print(msg, flush=True)

        def load_wav16(path):
            with wave.open(path, "rb") as w:
                if w.getframerate() != SAMPLE_RATE or w.getnchannels() != 1:
                    raise SystemExit("Expected 16 kHz mono WAV.")
                raw = w.readframes(w.getnframes())
            return np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0

        def main():
            ap = argparse.ArgumentParser()
            ap.add_argument("--audio", required=True)
            ap.add_argument("--output", required=True)
            ap.add_argument("--device", default="cuda")
            ap.add_argument("--compute-type", default="int8")
            ap.add_argument("--model", default="large-v3")
            ap.add_argument("--language", default="auto")
            ap.add_argument("--lyrics", default=None)
            args = ap.parse_args()

            import torch
            import whisperx

            device = args.device
            if device == "cuda" and not torch.cuda.is_available():
                device = "cpu"
            log(f"[whisperx] device={device} compute_type={args.compute_type}")

            audio = load_wav16(args.audio)
            duration = len(audio) / SAMPLE_RATE
            lang = args.language if args.language and args.language != "auto" else None

            asr_options = {}
            if args.lyrics:
                with open(args.lyrics, "r", encoding="utf-8") as f:
                    text = " ".join(line.strip() for line in f if line.strip())
                if text:
                    prompt = text[:1000] if len(text) > 1000 else text
                    asr_options["initial_prompt"] = prompt
                    log(f"[whisperx] Using lyrics as initial_prompt ({len(prompt)} chars).")

            log(f"[whisperx] Loading {args.model}…")
            model = whisperx.load_model(
                args.model, device, compute_type=args.compute_type, language=lang, asr_options=asr_options)
            
            log("[whisperx] Transcribing…")
            result = model.transcribe(audio, batch_size=8)
            lang = result.get("language", lang or "en")
            segments = result["segments"]
            log(f"[whisperx] Transcribed {len(segments)} segments; language={lang}")

            if args.lyrics and segments:
                with open(args.lyrics, "r", encoding="utf-8") as f:
                    official_text = " ".join(line.strip() for line in f if line.strip())
                
                if official_text:
                    log("[whisperx] Distributing official lyrics to VAD segments (True Forced Alignment)…")
                    import re, difflib
                    off_words = re.findall(r"[\w']+|[.,!?;]", official_text)
                    
                    trans_words = []
                    word_to_seg = []
                    for i, seg in enumerate(segments):
                        words = re.findall(r"[\w']+|[.,!?;]", seg["text"])
                        trans_words.extend(words)
                        word_to_seg.extend([i] * len(words))
                        
                    sm = difflib.SequenceMatcher(None, [w.lower() for w in trans_words], [w.lower() for w in off_words])
                    
                    off_to_seg = [-1] * len(off_words)
                    for opcode, i1, i2, j1, j2 in sm.get_opcodes():
                        if opcode in ('equal', 'replace'):
                            for k in range(j2 - j1):
                                t_idx = i1 + int((k / (j2 - j1)) * (i2 - i1)) if i2 > i1 else i1 - 1
                                if t_idx >= len(word_to_seg): t_idx = len(word_to_seg) - 1
                                if t_idx >= 0:
                                    off_to_seg[j1 + k] = word_to_seg[t_idx]
                        elif opcode == 'insert':
                            t_idx = i1 - 1
                            if t_idx >= len(word_to_seg): t_idx = len(word_to_seg) - 1
                            if t_idx >= 0:
                                for k in range(j1, j2):
                                    off_to_seg[k] = word_to_seg[t_idx]
                                    
                    last_seg = 0
                    for i in range(len(off_to_seg)):
                        if off_to_seg[i] != -1:
                            last_seg = off_to_seg[i]
                        else:
                            off_to_seg[i] = last_seg
                            
                    seg_texts = [[] for _ in segments]
                    for w, seg_idx in zip(off_words, off_to_seg):
                        if seg_idx != -1:
                            seg_texts[seg_idx].append(w)
                            
                    for i, seg in enumerate(segments):
                        text = ""
                        for w in seg_texts[i]:
                            if re.match(r"^[.,!?;]$", w):
                                text += w
                            else:
                                text += " " + w if text else w
                        seg["text"] = text.strip()

            log("[whisperx] Loading alignment model…")
            try:
                align_model, metadata = whisperx.load_align_model(language_code=lang, device=device)
            except Exception as e:
                log(f"[whisperx] Warning: failed to load alignment model for {lang}: {e}")
                log("[whisperx] Falling back to English for alignment…")
                lang = "en"
                align_model, metadata = whisperx.load_align_model(language_code=lang, device=device)
                
            log("[whisperx] Aligning…")
            aligned = whisperx.align(
                segments, align_model, metadata, audio, device, return_char_alignments=False)

            words = []
            for seg in aligned.get("segments", []):
                for w in seg.get("words", []):
                    if "start" not in w or "end" not in w:
                        continue
                    words.append({
                        "text": w.get("word", ""),
                        "start": float(w["start"]),
                        "end": float(w["end"]),
                        "score": float(w.get("score", 1.0)),
                    })

            with open(args.output, "w", encoding="utf-8") as f:
                json.dump({"language": lang, "words": words}, f)
            log(f"[whisperx] Done — {len(words)} words.")

        if __name__ == "__main__":
            main()
        """;
}
