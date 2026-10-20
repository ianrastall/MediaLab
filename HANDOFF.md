# LyricsLab WhisperX Handoff & Audit Snapshot

## Current Status
We are building a C# desktop application (`LyricsLab`) that provides word-level lyric alignment using **WhisperX** (via a Python driver script). The goal is to take a song's audio and a known set of lyrics, and return highly accurate, word-level timestamps. 

Currently, the word-level alignment is producing highly inaccurate timestamps (e.g., words stretching across massive instrumental gaps) or completely hallucinates the text when transcribing.

## Architecture & Workflow
1. **C# Service:** `WhisperXTranscriberService.cs` extracts the known lyrics and invokes a bundled Python virtual environment.
2. **Python Driver:** `whisperx_driver.py` is invoked with `--audio`, `--lyrics`, and `--model`. It uses the `whisperx` library to perform transcription and alignment.
3. **C# Aligner:** `LyricsAligner.cs` receives the JSON output of timed words and uses a Needleman-Wunsch dynamic programming algorithm to match the transcribed words back to the official lyrics.

## Recent Attempts & Issues

### 1. The Forced-Alignment Stretching Bug
Initially, the python driver used WhisperX's forced-alignment mode (skipping transcription and pushing a single segment `[{"start": 0.0, "end": duration, "text": text}]` directly to `whisperx.align()`). 
* **Result:** Because the entire text was grouped into a single massive segment, the alignment model stretched words to fill instrumental gaps (e.g., 14-second durations for a single word).

### 2. Transcribe-and-Align (Large-v3)
To fix the stretching, we switched the driver to run standard transcription first, passing the known lyrics to the `initial_prompt` argument, followed by alignment.
* **Result:** Because the user's environment falls back to CPU execution, the default `large-v3` model took an unreasonable amount of time (hanging/processing indefinitely).

### 3. Transcribe-and-Align (Tiny)
To bypass the massive CPU bottleneck, we changed the hardcoded model to `tiny`. 
* **Result:** The `tiny` model completed quickly but generated a highly degraded, hallucinated transcription (`Runnin' out, runnin' I don't belong here...`), which made the downstream Needleman-Wunsch alignment algorithm fail to match the official lyrics correctly.

## Environmental Details & The "Missing DLLs" Warning
During execution, Pyannote (the Voice Activity Detection library used by WhisperX) throws the following warning:
```
UserWarning: torchcodec is not installed correctly so built-in audio decoding will fail.
[start of libtorchcodec loading traceback]
Could not find module '...libtorchcodec_core7.dll' (or one of its dependencies).
```
**Agent Note on the DLLs:** Pyannote throws this warning when `torchcodec` fails to load. Pyannote's internal logic is designed to gracefully fall back to `torchaudio` or `soundfile` for audio decoding when `torchcodec` is unavailable. While this warning looks alarming, it is a known fallback mechanism and generally does not prevent the VAD or transcription models from running. However, it has been noted for the audit.

## Questions for Outside Audit
1. **WhisperX Workflow for Known Lyrics:** What is the industry standard approach for forcing WhisperX to align perfectly to a known lyrics sheet *without* it stretching words across long instrumental gaps?
2. **CPU Performance:** If GPU/CUDA is unavailable, is there a recommended model size or parameter configuration for `whisperx` that balances accurate transcription (so `align` doesn't fail) with acceptable CPU execution times?
3. **DLL / VAD Impact:** Does the missing `libtorchcodec_core7.dll` warning indicate a deeper underlying issue with how Pyannote slices the audio, which might be contributing to the degraded transcription output in CPU mode?
