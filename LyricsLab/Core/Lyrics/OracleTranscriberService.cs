using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lab.Core.Tools;

namespace LyricsLab.Core.Lyrics
{
    public class OracleTranscriberService
    {
        private readonly Func<string, string?, string, Action<string>, CancellationToken, Task<IReadOnlyList<TimedWord>>>? _runWhisperX;
        private readonly Func<string, string?, string, Action<string>, CancellationToken, Task<IReadOnlyList<TimedWord>>>? _runWhisper;
        private readonly Func<string, string?, Action<string>, CancellationToken, Task<IReadOnlyList<TimedWord>>>? _runVosk;

        public OracleTranscriberService(ToolLocator tools, Lab.Core.Processes.IProcessRunner? runner = null)
        {
            var whisper = new WhisperTranscriberService(tools, runner);
            var vosk = new VoskTranscriberService(tools, runner);
            var whisperx = new WhisperXTranscriberService(tools, runner);
            var voskModelDir = vosk.FindModel();
            
            if (whisperx.IsInstalled())
                _runWhisperX = (audio, hint, model, onLog, ct) => whisperx.TranscribeAsync(audio, hint, "auto", true, model, onLog, ct);
            
            if (whisper.FindModel() != null)
                _runWhisper = (audio, hint, model, onLog, ct) => {
                    var m = whisper.FindModel(model) ?? whisper.FindModel();
                    if (m == null) throw new InvalidOperationException("Whisper model not found.");
                    return whisper.TranscribeAsync(audio, m, "auto", hint, onLog, ct);
                };
            
            if (voskModelDir != null)
                _runVosk = (audio, hint, onLog, ct) => vosk.TranscribeAsync(audio, voskModelDir, hint, onLog, ct);
        }

        internal OracleTranscriberService(
            Func<Task<IReadOnlyList<TimedWord>>>? runWhisperX,
            Func<Task<IReadOnlyList<TimedWord>>>? runWhisper,
            Func<Task<IReadOnlyList<TimedWord>>>? runVosk)
        {
            if (runWhisperX != null) _runWhisperX = (_, _, _, _, _) => runWhisperX();
            if (runWhisper != null) _runWhisper = (_, _, _, _, _) => runWhisper();
            if (runVosk != null) _runVosk = (_, _, _, _) => runVosk();
        }

        public async Task<IReadOnlyList<TimedWord>> TranscribeOracleAsync(
            string audioPath, string? lyricsHint, string model, Action<string> onLog, CancellationToken ct = default)
        {
            Task<IReadOnlyList<TimedWord>>? whisperxTask = null;
            Task<IReadOnlyList<TimedWord>>? whisperTask = null;
            Task<IReadOnlyList<TimedWord>>? voskTask = null;

            var pending = new List<Task<IReadOnlyList<TimedWord>>>();

            if (_runWhisperX != null)
            {
                onLog("[oracle] Starting WhisperX...");
                whisperxTask = _runWhisperX(audioPath, lyricsHint, model, onLog, ct);
                pending.Add(whisperxTask);
            }

            if (_runWhisper != null)
            {
                onLog("[oracle] Starting Whisper...");
                whisperTask = _runWhisper(audioPath, lyricsHint, model, onLog, ct);
                pending.Add(whisperTask);
            }

            if (_runVosk != null)
            {
                onLog("[oracle] Starting Vosk...");
                voskTask = _runVosk(audioPath, lyricsHint, onLog, ct);
                pending.Add(voskTask);
            }

            if (pending.Count == 0)
                throw new InvalidOperationException("No speech engine available. Install models via Setup.");

            try 
            { 
                await Task.WhenAll(pending); 
            } 
            catch { /* Ignore here, handled below */ }

            foreach (var task in pending)
            {
                if (task.IsFaulted)
                {
                    var msg = task.Exception?.InnerException?.Message ?? task.Exception?.Message;
                    onLog($"[oracle] Engine failed: {msg}");
                }
            }

            var results = new List<IReadOnlyList<TimedWord>>();
            if (whisperxTask?.IsCompletedSuccessfully == true) results.Add(whisperxTask.Result);
            if (whisperTask?.IsCompletedSuccessfully == true) results.Add(whisperTask.Result);
            if (voskTask?.IsCompletedSuccessfully == true) results.Add(voskTask.Result);

            if (results.Count == 0)
                throw new InvalidOperationException("All speech engines failed.");

            if (results.Count == 1)
            {
                onLog($"[oracle] Used single successful engine output ({results[0].Count} words).");
                return results[0];
            }

            // Order results by word count descending to determine the richest transcript
            results.Sort((a, b) => b.Count.CompareTo(a.Count));
            var primary = results[0];
            var secondary = results[1];

            if (secondary.Count < primary.Count * 0.4)
            {
                onLog($"[oracle] Secondary engine had only {secondary.Count} words vs {primary.Count}. Using primary alone.");
                return primary;
            }

            return TranscriptMerger.MergeTranscripts(primary, secondary, onLog);
        }
    }
}
