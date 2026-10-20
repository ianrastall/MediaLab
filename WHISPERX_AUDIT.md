# LyricsLab V2 (WhisperX) — Outside Audit

**Scope:** the WhisperX path added on top of LyricsLab, plus the parts of the suite it touches
(`WhisperXTranscriberService`, the embedded `whisperx_driver.py`, `WhisperInstallerService`,
`TranscriptionViewModel.RunEngineAsync`, `LyricsAligner`). Findings are ordered by severity.
File references are by class/method since the snapshot concatenates files.

---

## TL;DR — the one thing to take away

Your reported symptom (hallucinated text, words smeared across instrumental gaps) is not really an
alignment bug. It traces to **two decisions that fight the architecture you designed**:

1. The WhisperX driver is hardcoded to the **`tiny`** model, which produces garbage on singing. Your
   design says "Whisper only needs to find *roughly when* words happen; the official lyrics supply
   the text." That's correct — but it depends on the transcription being good enough to *anchor*. The
   C# Needleman–Wunsch aligner only records a match when token similarity ≥ 0.70. `tiny`'s
   hallucinations clear that threshold almost never, so nearly every line falls through to
   `FillUnanchored`, which interpolates across the big silent stretches → the "14-second word" smear.

2. The WhisperX path **silently disables the much better-tuned whisper.cpp + Vosk pipeline you
   already built.** `RunEngineAsync` returns from the WhisperX branch first, so the moment WhisperX
   is installed, none of your hallucination-loop filtering, trailing-phrase stripping, sane model
   auto-selection, or dual-engine merge ever runs. **Installing V2 made the app worse, not better.**

Everything below expands on this, but those two points are the crux. The single highest-value change
is C1 (stop using `tiny`); the most important *decision* is C3 (whether V2 should exist at all).

---

## CRITICAL

### C1 — WhisperX model is hardcoded to `tiny`
**Where:** `WhisperXTranscriberService.TranscribeAsync`, in the args list:
```csharp
"--compute-type", "int8",
"--model", "tiny",          // <- this
"--language", language,
```
**Symptom:** hallucinated transcript (`Runnin' out, runnin' I don't belong here…`), which the aligner
can't match to the real lyrics. Note the driver's own default is `large-v3`; the C# is overriding it
down to the worst possible model.

**Why it breaks the whole premise:** the aligner's match gate is `MatchThreshold = 0.7` Levenshtein
similarity. `tiny` rarely produces a word within edit-distance 0.7 of the correct one, so
`matchedPerLine` stays ~0 for most lines, `startSec`/`endSec` stay null, and `FillUnanchored`
spreads those lines evenly between the few anchors it has — straight across instrumental gaps.

**Fix (minimal):** use a model that actually anchors. On CPU with `int8`, `base` (or `base.en` for
English) is the floor that produces usable timings; `small`/`small.en` is the sweet spot. Make it a
parameter rather than a constant:

```csharp
public async Task<IReadOnlyList<TimedWord>> TranscribeAsync(
    string audioPath,
    string? lyricsText = null,
    string language = "auto",
    bool preferGpu = true,
    string model = "small",          // NEW — default to something that works
    Action<string>? onLogLine = null,
    CancellationToken cancellationToken = default)
{
    ...
    "--model", model,
    ...
}
```
…and pick a CPU-appropriate default in the view-model (see H2). For English content, append `.en`
(`base.en`, `small.en`) — the English-only models are meaningfully more accurate at the same size.

---

### C2 — The documented "force-align to known lyrics" mode does not exist
**Where:** `WhisperXTranscriberService` class doc + the `<param name="lyricsText">` doc:
> *"When non-empty, whisperX force-aligns these instead of transcribing."*

The code never does that. It always transcribes (with `tiny`) and then aligns the **transcribed**
segments. The known lyrics are used *only* as `initial_prompt` (truncated to 1000 chars) inside the
driver. So the project's headline insight — "use the official lyrics as the alignment text" — is
unimplemented in the V2 path. The text driving every word timing comes 100% from `tiny`'s guesses.

This is the architectural miss that makes C1 fatal instead of merely degrading. If the official text
were genuinely the alignment target, model quality would barely matter. As written, model quality is
*everything*, because the official text is only a weak prompt.

**Fix — two options, pick one deliberately:**

- **(a) Keep your C# aligner, just give it real anchors.** This is the smaller change and it's
  consistent with the rest of the app: do C1, and the existing transcribe→match→interpolate flow
  starts working because most lines now anchor. The interpolation gaps shrink to the genuinely
  un-sung lines.

- **(b) Do true forced alignment (the "industry standard" you asked about in Q1).** WhisperX's
  `align()` confines each segment's text to *that segment's time window*. Your original stretching
  bug (HANDOFF #1) happened because you fed it **one** segment spanning the whole song. The fix is
  never one segment — run VAD (or a rough ASR pass) to get speech regions, distribute the official
  lyric lines across those regions, then `align()` per region. Words then physically cannot stretch
  into a gap because the gap isn't inside any segment's window. The catch: deciding *which lines go
  in which region* still requires a transcription good enough to match against — i.e. you still can't
  use `tiny`. The two approaches converge on the same prerequisite.

Either way: **a usable transcription is mandatory.** That's the real answer to "how do I stop the
stretching." It isn't a forced-vs-transcribe toggle; it's that every method needs honest anchors.

Until you implement (b), fix the lying docs so the next reader isn't misled about what the code does.

---

### C3 — WhisperX shadows the good pipeline you already built
**Where:** `TranscriptionViewModel.RunEngineAsync`:
```csharp
if (_whisperxInstalled)
{
    try { return await _whisperx.TranscribeAsync(...); }   // returns here, always
    catch (Exception ex) { /* fall back */ }
}
// whisper.cpp + Vosk path below never runs if WhisperX succeeded
```
The whisper.cpp service (`WhisperTranscriberService`) is genuinely well-built: it auto-selects a sane
model (`small > base > medium > tiny > large`, deliberately ranking `large` last),
seeds `--prompt` with the lyrics, runs `FilterHallucinationLoops` (drops frozen-clock and
backward-jumping words) and `FilterTrailingHallucinations` (strips "Thanks for watching" etc.), and
warns when timestamps collapse to ~0:00. The Vosk service adds grammar-constrained recognition and a
speech-shaping ffmpeg filter. `RunEngineAsync` even merges the two with a confidence floor.

**All of that is dead code the moment WhisperX is installed** — because the WhisperX branch returns
first, and only on an *exception* falls through. A merely-bad WhisperX result (which is the normal
case with `tiny`) is not an exception, so it wins.

**Fix:** make engine choice explicit instead of "WhisperX first, silently." Options:
- Add an engine selector to the UI and let WhisperX be *opt-in*, only chosen when it's configured
  with a real model + GPU.
- Or, honestly: **consider dropping the WhisperX path entirely.** Your whisper.cpp + Vosk pipeline is
  where all the careful music-specific tuning lives. WhisperX's advantage (wav2vec2 refinement) is
  real, but you're not getting it — you're getting `tiny` with none of your filters. Unless you're
  prepared to bring V2 to parity (filters, model selection, true forced alignment), the V1 pipeline
  is the better product today.

---

### C4 — The whisperX venv install is unpinned, CUDA-forced, and order-fragile
**Where:** `WhisperInstallerService.InstallWhisperXAsync`:
```csharp
// torch from the CUDA-12.1 index, unconditionally:
["-m","pip","install","torch","torchaudio","--index-url","https://download.pytorch.org/whl/cu121"]
// then, separately, with no versions:
["-m","pip","install","whisperx"]
```
Three problems compounding:

1. **CUDA build forced even on CPU-only machines.** ~2.5 GB of CUDA wheels that never load on your
   box (the HANDOFF says you fall back to CPU). Wasteful, and it sets up the next problem.
2. **No version pinning + two-pass install.** WhisperX has tight transitive pins (ctranslate2,
   faster-whisper, pyannote.audio, a specific torch range). Installing torch first and `whisperx`
   second lets pip re-resolve and *replace* your carefully-chosen torch with whatever whisperx's deps
   want — frequently a different CUDA/cuDNN combination than the torch wheel ships, which surfaces at
   runtime as "cublas/cudnn DLL not found" or ctranslate2 load failures. This is exactly the class of
   "broken environment" the handoff is fighting, and it's non-reproducible run to run.
3. The driver's `initial_prompt` / `language=` kwargs and `asr_options` handling are version-
   sensitive in whisperX; with no pin you're at the mercy of whatever PyPI serves that day.

**Fix:** pick a known-good matrix and install it in *one* resolver pass; only reach for CUDA torch
when a GPU is actually present.
```text
# detect GPU first (nvidia-smi present?), then:
# CPU box:
pip install "torch==<pinned>" "torchaudio==<pinned>" --index-url https://download.pytorch.org/whl/cpu
pip install "whisperx==<pinned>"
# GPU box: same but cu121 index, with a torch version known to match whisperx's ctranslate2/cuDNN.
```
Or drive it from a constraints file so the whole graph resolves together. Pin every layer
(torch, torchaudio, ctranslate2, faster-whisper, pyannote.audio, whisperx). The "models download on
first use" note is fine to keep; it's the *package* graph that needs nailing down.

---

## HIGH

### H1 — WhisperX-only setup turns a hiccup into a misleading hard failure
In `RunEngineAsync`, the WhisperX `catch` says *"falling back to whisper.cpp + Vosk."* But if the
user installed **only** WhisperX (no ggml model, no Vosk model — a perfectly normal V2 setup), the
fallback immediately hits:
```csharp
throw new InvalidOperationException("No speech engine available. Install Whisper or Vosk models...");
```
So a transient WhisperX error is reported as "no engine installed," which is false and unactionable.
**Fix:** if WhisperX is the only installed engine, surface the actual WhisperX exception rather than
the generic "install something" message.

### H2 — `preferGpu` and model are both hardcoded; the user can't pick either
`RunEngineAsync` always calls `TranscribeAsync(..., preferGpu: true, ...)`, and the model is the
`tiny` constant from C1. There is no UI to choose a model size or force CPU, even though the service
*has* a `preferGpu` parameter and the language picker already exists right next to it.
**Fix:** expose a model dropdown (mirror the whisper.cpp `KnownModels` idea — tiny/base/small/medium,
`.en` variants) and a GPU/CPU toggle, defaulting model to `small` (or `base` on low-RAM machines) and
GPU to "auto." This is also the natural home for the C1 default.

### H3 — `initial_prompt` does little in batched WhisperX, and the 1000-char cut is arbitrary
The driver puts the lyrics into `asr_options["initial_prompt"]`. In WhisperX's batched VAD pipeline,
prompt conditioning is limited (it doesn't carry across all chunks the way sequential whisper does),
so even with a bigger model this is a weak lever — it is *not* a substitute for using the lyrics as
the alignment text (C2). The `text[:1000]` truncation is a magic number with no rationale.
**Fix:** don't lean on `initial_prompt` for correctness; treat it as a mild bias only. If you want the
lyrics to actually drive the output text, that's C2(b). If you keep the prompt, at least truncate on a
word boundary and document the budget.

### H4 — `language: "auto"` can crash the alignment-model load
Driver flow:
```python
lang = result.get("language", lang or "en")
align_model, metadata = whisperx.load_align_model(language_code=lang, device=device)
```
On a heavily-instrumental track, `tiny` can mis-detect the language; `load_align_model` then throws
`No default align-model for language: X` and the whole run dies with an opaque message.
**Fix:** when the user picked "auto," prefer the UI-selected language if any, else default to `"en"`;
and wrap `load_align_model` so an unsupported detection degrades gracefully (skip alignment, return
the raw faster-whisper word times) instead of failing the run.

---

## MEDIUM

### M1 — `py` launcher may select an unsupported Python (3.13+)
`ResolveBasePython` returns bare `py` (or `python`) with no version. The error text says "install
Python 3.10–3.12," but nothing enforces it — if the machine's default `py` is 3.13, the torch wheel
install in C4 fails with "no matching distribution," and the user gets a pip error instead of a clear
"unsupported Python" message.
**Fix:** probe for a supported minor explicitly (`py -3.12`, `py -3.11`, …) or read `--version` and
refuse with a clear message before creating the venv.

### M2 — Two parallel "model" concepts that don't talk to each other
`WhisperInstallerService.KnownModels` downloads **ggml** files (`ggml-base.en.bin`, etc.) for
whisper.cpp. WhisperX ignores those entirely and downloads its own **faster-whisper** models on first
use; `--model small` refers to the latter. A user who "installs the small model" via Setup and then
runs WhisperX has downloaded a file WhisperX never touches, and triggers a second silent download at
transcribe time. It's a coherent consequence of the V1→V2 bolt-on, but it's confusing and wasteful.
**Fix:** if WhisperX stays, make the Setup UI distinguish the two engines' model stores (or unify on
one). If WhisperX goes (C3), this resolves itself.

### M3 — Documentation contradicts the code in several places
- `docs/LYRICSLAB.md` describes `FindModel` priority as **`large (4) > medium (3) > small (2) >
  base (1)`** — the *exact inverse* of `WhisperTranscriberService.ModelScore`, which ranks
  `small (5) > base (4) > medium (3) > tiny (2) > large (1)`. One of these is wrong; the code is the
  one that runs.
- `docs/LYRICSLAB.md` "Step 1 — Transcription" describes the whisper.cpp pipeline as *the* pipeline
  and never mentions the WhisperX path that actually fronts everything now.
- `WhisperXTranscriberService`'s XML docs describe a forced-alignment mode the code doesn't implement
  (C2).

You care about doc fidelity, so flagging these explicitly: the docs currently describe a V1 app while
a V2 path quietly runs in front of it.

---

## LOW

### L1 — `finally` cleanup can mask the real exception
In all three transcriber services the temp-dir cleanup is:
```csharp
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ... }
```
Any *other* exception thrown by `Directory.Delete` inside the `finally` would propagate and replace
the original failure that's unwinding. Rare, but it's the kind of thing that turns a clear error into
a confusing one. Consider catching broadly in cleanup-only `finally` blocks (it's best-effort anyway).

### L2 — Vosk WAV reader can split 16-bit samples
`RunVosk` reads the PCM stream in 8192-byte chunks straight into `AcceptWaveform`. At EOF a read can
return an odd byte count, splitting a 16-bit sample; `SkipWavHeader` also doesn't handle the WAV
word-alignment pad byte on odd-sized chunks. Pre-existing, low impact (one sample at a boundary), but
worth a note since you're in here.

### L3 — The missing `libtorchcodec_core7.dll` warning is a genuine red herring
Your agent note is right, and I'll confirm it firmly so you stop chasing it: pyannote falls back to
`torchaudio`/`soundfile` when `torchcodec` fails to load, **and** your driver pre-decodes the WAV via
the stdlib `wave` module into a numpy array before anything touches torchcodec — so torchcodec is
never on the critical path for either VAD or transcription. The missing DLL does **not** cause the
degraded output. The degraded output is `tiny` (C1). If the warning noise bothers you, install a
`torchcodec` matching your torch build (it needs FFmpeg shared libs on PATH) or pin the torchaudio
backend explicitly — but it's cosmetic.

---

## Recommended end-state for the WhisperX path (if you keep it)

1. **Model:** default `small` / `small.en` on CPU (`int8`), allow `base` for low-RAM and `large-v3`
   only when CUDA is present. User-selectable (H2).
2. **Alignment:** implement C2(b) — VAD-segmented forced alignment of the *official* lyric lines, so
   the output text is exact and timings are window-bounded. Keep the C# Needleman–Wunsch only as the
   "no lyrics supplied" / sanity path.
3. **Install:** pinned, single-pass, GPU-conditional (C4); enforce supported Python (M1).
4. **Selection:** explicit, opt-in, never silently shadowing whisper.cpp (C3); honest errors (H1).

If you *don't* want to invest that, the defensible call is to retire V2 and keep refining the
whisper.cpp + Vosk pipeline — it already does the hard, music-specific work that V2 throws away.

---

## Direct answers to your three handoff questions

**Q1 — Industry-standard way to force alignment to known lyrics without stretching across gaps?**
Segment the known text and align per-segment within VAD-derived time windows; never feed one segment
spanning the whole song (that was your HANDOFF #1 bug). WhisperX's `align()` already respects segment
boundaries, so bounded segments can't stretch into silence. But every approach — forced alignment or
transcribe-then-match — needs a transcription good enough to place words, so step zero is dropping
`tiny`. See C2.

**Q2 — CPU model/config that balances accuracy and runtime?**
`small` (or `small.en` for English) with `compute_type="int8"` is the practical sweet spot on CPU;
`base`/`base.en` is the acceptable floor when RAM is tight. Avoid `large-v3` on CPU (your own Setup
warning already says 16 GB+ and "extremely slow"). Set `cpu_threads` to your physical core count and
keep `batch_size` modest (4–8). `int8` is already correct in your args. See C1/H2.

**Q3 — Does the missing `libtorchcodec_core7.dll` indicate a deeper VAD/slicing problem driving the
bad output?**
No. It's a benign fallback, and you bypass torchcodec entirely by pre-decoding the WAV in Python. The
bad output is the `tiny` model, not VAD slicing. See L3.

---

## What's already good (don't regress these)

- `WhisperTranscriberService`: model auto-selection, hallucination-loop and trailing-phrase filters,
  timestamp-collapse warning. This is the strongest part of the lyrics stack.
- `VoskTranscriberService`: grammar-constrained recognition + speech-shaping ffmpeg filter + the
  open-recognition fallback when a grammar word is out-of-vocab.
- `ProcessRunner`: concurrent stream draining (no pipe deadlock), process-tree kill on cancel,
  honoring a cancel that races a clean exit.
- `TerminalViewModel`: lock-free `ConcurrentQueue` + `DispatcherTimer` batching — correctly handles
  up to four concurrent pump threads (two engines × stdout/stderr) writing logs. This is exactly the
  thread-safety the parallel-engine design needs, and it's done right.
- `LyricsAligner`: the global Needleman–Wunsch core is sound; the leading-instrumental `safeMinStart`
  trim and the gap-aware `FillUnanchored` interpolation are thoughtful. It's let down only by the
  garbage it's being fed, not by its own logic.
