# MediaLab — Handoff Explainer

This document is the primary context document for a new session taking over MediaLab. It covers what the software is, how every part works and interoperates, what is wrong with it, what the target architecture looks like, and what must be done. Read this before touching anything.

---

## 1. What This Is

MediaLab is a personal Windows desktop media-utility suite. It is not a commercial product. It is one developer's toolbox for producing music-related content: timing lyrics to audio for subtitle files, rendering visualizer videos, mastering audio tracks, and preparing images for YouTube thumbnails.

It is a C# / WPF solution targeting .NET 10.0-windows. All apps are 64-bit only. The solution lives at `D:\GitHub\MediaLab` and is opened with Visual Studio 2022 via `MediaLab.slnx` (the newer solution XML format, not `.sln`).

The suite has five executable apps and two shared libraries:

| Project | Kind | Purpose |
|---------|------|---------|
| `Lab.Core` | Class library | Shared business logic, services, and infrastructure |
| `Lab.UI` | WPF library | Shared controls, converters, styles, and base ViewModels |
| `LyricsLab` | WinExe | Transcribe audio → align to lyrics → export subtitles |
| `VideoLab` | WinExe | Render an MP4 visualizer from audio + background image |
| `SongLab` | WinExe | Master an audio file (EQ → time-stretch → loudness normalize → encode) |
| `ImageLab` | WinExe | Non-destructive image editing (crop, resize, color adjust) via ImageMagick |
| `MediaLab.Hub` | WinExe | Launcher: a 2×2 tile grid that starts the other four apps |
| `MediaLab.Tests` | Test library | xUnit tests for Lab.Core (no UI tests) |

Build output goes to `Build\<ProjectName>\` flat folders, not nested bin/Debug/net10.0-windows paths. This is set in `Directory.Build.props` via `<OutputPath>`. The Hub launcher resolves sibling executables from its own assembly location using this convention.

---

## 2. Coding Conventions

These are consistent across the entire solution. A new session must follow them exactly.

**MVVM via CommunityToolkit.Mvvm 8.4.2.** Every ViewModel inherits `ObservableObject`. Properties use `[ObservableProperty]` source generators (generates the backing field, getter, setter, and `PropertyChanged` event automatically). Commands use `[RelayCommand]` (generates an `IRelayCommand` implementation; async methods get `AsyncRelayCommand` with built-in exception handling; `CanExecute` is wired via `CanExecute = nameof(SomeMethod)` on the attribute).

**No DI container.** There is no Autofac, Microsoft.Extensions.DI, or similar. Services are constructed directly in ViewModels or passed as constructor arguments. The only "locator" pattern is `ToolLocator` for finding external CLI executables.

**Immutable records for data.** `VideoRenderJob`, `AlignedLine`, `TimedWord`, `ImageAdjustments`, `LyricLineData`, `ProcessResult`, `InstrumentalGap` etc. are all C# `record` types. They are created once and not mutated. Modifications produce new instances via `with` expressions.

**Async/await throughout.** All I/O (file system, subprocess invocation, HTTP downloads) is async with `CancellationToken` support threaded through every layer. Long-running ViewModel commands should expose a cancel command using a `CancellationTokenSource` field.

**`IProcessRunner` abstraction.** Every CLI tool invocation (FFmpeg, SoX, Whisper, etc.) goes through `IProcessRunner`. This interface is in `Lab.Core.Processes`. It enables mocking in unit tests. Currently `VideoRenderService` violates this by taking the concrete `ProcessRunner` class — that is a known bug to fix.

**`ToolLocator` for finding executables.** Never hardcode a path to a CLI tool. Use `ToolLocator.Locate("toolname")` which searches: bundled `bin/` folder → configured explicit path → PATH env var → portable `tools/` subdirectory → legacy dev folder.

**Terminal/log pattern.** Every app has a `TerminalViewModel` (from `Lab.UI`) at the bottom of its window. All service log output is routed to `terminal.Append(line)`. The `TerminalViewModel` is thread-safe (uses `ConcurrentQueue` + `DispatcherTimer`). Never `Dispatcher.Invoke` from a service to update UI — route through the terminal instead.

**Partial methods for property change side effects.** CommunityToolkit generates `partial void OnXxxChanged(T value)` hooks. Use these for derived-property logic (e.g., when audio path changes, derive the output path). Do not subscribe to `PropertyChanged` events on self.

**Dark theme only.** All apps use `DarkTheme.xaml` from Lab.UI. Every color reference uses a `DynamicResource` or `StaticResource` that resolves from the theme dictionary (never hardcoded hex in XAML except `#FF181818` for the log panel background, which is a product constant). The shared named resources are: `WindowBackgroundBrush`, `PanelBackgroundBrush`, `ControlBackgroundBrush`, `ControlBackgroundHoverBrush`, `AccentBrush`, `ForegroundBrush`, `SubtleForegroundBrush`, `BorderBrush`, `TextForegroundBrush`, `SubtleTextBrush`, `SubtleBorderBrush`, `HoverBrush`. Named styles: `HeaderTextBlockStyle`, `SubtleTextBlockStyle`, `LogTextStyle`, `AccentButtonStyle`.

**Consistent app shell layout.** Every app window follows the same four-row grid pattern: (1) header bar with title + tool-status chip, (2) main content area with left controls + right preview, (3) draggable `GridSplitter`, (4) terminal/log panel. The Hub is the only exception (it has no log panel).

**C# language version:** 14. Nullable enabled. Implicit usings enabled. x64 platform target. All these are set globally in `Directory.Build.props` and must not be overridden per project.

---

## 3. External Tool Dependencies

Every app delegates heavy-lifting to CLI tools. The `ToolLocator` finds them; the `DependencyVerifier` checks for them at startup to populate the header status chip.

| Tool name | Used by | What it does |
|-----------|---------|-------------|
| `ffmpeg` | VideoLab, SongLab, LyricsLab | Encode/mux video and audio; convert audio to 16 kHz WAV for transcription |
| `ffprobe` | VideoLab, LyricsLab, ImageLab | Query media duration; query image dimensions |
| `magick` (ImageMagick) | ImageLab | All image processing: crop, resize, color adjustments, export |
| `sox` | SongLab | Audio DSP: high-pass, low-pass, bass, treble filters |
| `rubberband-r3` | SongLab | Time-stretching/tempo change (Rubber Band R3 engine) |
| `rsgain` | SongLab | Loudness normalization to a LUFS target (ReplayGain 2.0 / EBU R128) |
| WhisperX Python venv | LyricsLab | Speech-to-text with forced word-level alignment |
| Whisper CLI | LyricsLab | OpenAI Whisper standalone (fallback) |
| Vosk (NuGet + offline model) | LyricsLab | Fully offline speech recognition (fallback) |
| pyannote VAD (inside WhisperX venv) | LyricsLab | Voice activity detection for instrumental gap detection |

Tools can be bundled (in a `bin/` subfolder next to the exe), on PATH, or discovered via the portable `tools/` convention. `FfmpegInstallerService` and `WhisperInstallerService` can download and install them from within the app (Setup windows).

---

## 4. Lab.Core — What It Contains

`Lab.Core` is a class library with no WPF dependency (`net10.0`, not `net10.0-windows`). It currently holds both **infrastructure** (which belongs here) and **domain logic** (which does not — see Section 8 on the architecture problem).

### 4.1 Infrastructure (correctly placed)

**`Processes/`**
- `IProcessRunner` — interface: `RunAsync(exe, args, workingDir, onOutputLine, onErrorLine, captureOutput, ct) → Task<ProcessResult>`
- `ProcessRunner` — concrete implementation using `System.Diagnostics.Process`. Handles encoding, line buffering, cancellation.
- `ProcessResult` — record: `ExitCode`, `Success` (exit code 0), `Canceled`, `StandardOutput`, `StandardError`.

**`Tools/`**
- `ToolLocator` — multi-stage tool finder (bundled → configured → PATH → defaults). Fully injectable for testing.
- `ToolDefaults` — constants for portable tool subdirectory names and the legacy dev root `D:\cli\tools`.
- `DependencyVerifier` — scans for all required tools and returns `DependencyStatus[]`. Used by `ToolStatusViewModel`.
- `FfmpegInstallerService` — downloads FFmpeg from gyan.dev, extracts to portable `tools/` directory.
- `ZipInstallerService` — generic async zip downloader/extractor.

**`Logging/`**
- `BoundedLogBuffer` — thread-safe circular buffer. Caps at a configured line count. `TerminalViewModel` in Lab.UI wraps this.

### 4.2 Domain Logic (currently here, should move to each app — see Section 8)

**`Lyrics/`** — belongs in LyricsLab
- `SongLyrics`, `LyricSection`, `LyricLineData`, `FlatLyricLine` — the structured lyric data model. Sections can be vocal (verse/chorus/bridge/etc.) or instrumental. `IsReference` sections point to a previously-defined section by id, enabling chorus repetitions without duplicating lines. `VocalLines()` flattens the section tree into a time-ordered list, resolving references, skipping instrumentals — this is what the aligner receives.
- `LyricsXmlParser` — static parser. Reads the custom XML lyric format (described below in Section 5.1) into `SongLyrics`.
- `LyricsXmlWriter` — static serializer. Writes `SongLyrics` back to XML, preserving anchors, timing data, and relative distances.
- `LyricsAligner` — static aligner. Takes `SongLyrics` + `TimedWord[]` transcript and returns `AlignedLine[]` with start/end times. Uses Needleman-Wunsch global dynamic programming at the token level, with fuzzy Levenshtein similarity (0.7 match threshold). Skips instrumental sections. Supports manual anchors (lines the user has locked to known times). Interpolates unmatched lines between anchored neighbors.
- `TimedWord` — record struct: `Text`, `Start` (TimeSpan), `End` (TimeSpan), `Confidence` (float 0–1).
- `AlignedLine` — record: `Text`, `Start`, `End`, `MatchRatio`, `Confidence` (High/Medium/Low), `SectionType`, `MatchedTokens`, `TotalTokens`.
- `AlignerOptions` — tunable record: `MatchThreshold`, `GapPenalty`, `MinLineDuration`, `TotalDuration`, `DefaultLineSeconds`.
- `TimingIssue` / `TimingIssueDetector` — detect overlaps, gaps, long cues in an `AlignedLine[]`.
- `SubtitleWriter` — static. Converts `AlignedLine[]` to SRT, LRC, WebVTT, ASS, SubViewer (SBV), MicroDVD, or JSON strings.
- `SpeechModelInfo` — record describing an available model (engine, name, size, installed status).
- `TranscriptMerger` — merges two transcripts by adding high-confidence secondary words that are not duplicates of primary words (within 200ms window). Used by OracleTranscriberService.
- `OracleTranscriberService` — runs WhisperX + Whisper + Vosk in parallel, waits for all, picks the richest result, merges if multiple succeed.
- `WhisperXTranscriberService` — invokes the WhisperX Python venv. Contains `DriverScript` as a C# string constant (the Python source code, written to disk at install time). Converts audio to 16 kHz WAV via FFmpeg, then runs the driver script.
- `WhisperTranscriberService` — wraps the Whisper CLI executable.
- `VoskTranscriberService` — wraps Vosk offline speech recognition.
- `WhisperCppJsonParser` — parses the JSON word-level output format shared by Whisper and WhisperX.
- `WhisperInstallerService` — downloads/installs WhisperX venv and Whisper models.

**`Media/`** — domain logic split across three apps (VideoLab, SongLab) and partially LyricsLab
- `VideoRenderJob` — immutable record defining one FFmpeg render task.
- `VideoRenderService` — orchestrates a full FFmpeg video render: validate → probe → build filter graph → render to `.partial.mp4` → promote. Uses `SpectrumBarFilterBuilder` and `FfmpegProgressParser`.
- `SpectrumBarFilterBuilder` — generates the FFmpeg `lavfi` filter-graph string for the spectrum-bar visualizer (30 Hz highpass, logarithmic scaling, multi-bar overlap, peak-color overlay).
- `FfprobeService` — wraps ffprobe to return `TimeSpan?` duration.
- `ImageProbeService` — wraps ffprobe to return `(int W, int H)?` image dimensions.
- `FfmpegProgressParser` — stateful line parser extracting `out_time` from FFmpeg stderr to produce 0–100 percent values.
- `MasteringPipeline` — audio mastering orchestrator: decode (FFmpeg) → SoX DSP → Rubberband R3 → rsgain → encode (FFmpeg).
- `OutputPathBuilder` — derives output filenames (adds `_master` suffix, `.partial` extension).
- `ColorMath` — RGB utilities: parse hex strings, compute 30%-darker peak color for spectrum overlay.
- `InstrumentalDetectorService` — invokes `vad_detect.py` (the Python VAD script) via ProcessRunner, parses its JSON output, returns `InstrumentalGap[]`.

**`Imaging/`** — belongs in ImageLab
- `ImageAdjustments` — immutable record of all non-destructive edit parameters.
- `ImageMagickArgsBuilder` — translates `ImageAdjustments` into `magick` command-line arguments.
- `ImageOpsService` — runs `magick` via ProcessRunner.

**`Scripts/`**
- `vad_detect.py` — Python script that loads audio via WhisperX, runs pyannote Voice Activity Detection, outputs JSON non-vocal gaps. Invoked by `InstrumentalDetectorService`.

---

## 5. Lab.UI — What It Contains

`Lab.UI` is a WPF class library (`net10.0-windows`). It holds reusable UI components shared by all four apps.

**`Controls/`**
- `DarkColorPicker.xaml` / `.cs` — custom HSV color picker. Exposes `SelectedColor` as an `RgbColor` (a simple struct in Lab.Core). Has hue gradient bar, saturation/value canvas, recent-colors strip, hex text input.
- `AutoScrollBehavior.cs` — attached behavior that scrolls a ListBox to its selected item automatically when `IsActive` changes.
- `ColorConversions.cs` — WPF `Color` ↔ HSV tuple.

**`Converters/`**
- `BoolToVisibilityConverter` — supports `ConverterParameter=invert`.
- `InverseBoolConverter`
- `ColorToBrushConverter` — `RgbColor → SolidColorBrush`.
- `NullOrEmptyToVisibilityConverter` — `string? or ICollection? → Visibility`.

**`Services/`**
- `IFilePickerService` — `PickOpenFile`, `PickSaveFile`, `RevealInExplorer`.
- `FilePickerService` — WPF implementation using `OpenFileDialog`/`SaveFileDialog` and `Process.Start("explorer.exe", ...)`.

**`ViewModels/`**
- `TerminalViewModel` — observable log surface. Thread-safe: accepts `Append(string)` from any thread via `ConcurrentQueue`; flushes to `ObservableCollection<string> Lines` on a 100ms `DispatcherTimer`. Cap: 500 visible lines (trims from the top using clear+re-add to avoid O(n²) shifts). Has `CopyCommand` (copies full buffer to clipboard) and `ClearCommand`.
- `ToolStatusViewModel` — wraps `DependencyVerifier`. Exposes `AllPresent` (bool) and `StatusText`. Used in every app's header chip.

**`Themes/`**
- `DarkTheme.xaml` — master resource dictionary. All color brushes and shared styles. Every app merges this in its `App.xaml`.

---

## 6. The Four Apps — How Each Works

### 6.1 LyricsLab

**Purpose:** Given an audio file and a set of song lyrics, produce a subtitle file (SRT, LRC, etc.) where each lyric line is timed to the audio.

**Window layout (`MainWindow.xaml`):** Three rows (2:auto:1).
- Top-left (1/3): Audio + lyrics setup. Pick audio file; NAudio `AudioFileReader` + `WaveOutEvent` loads it for in-app playback. Scrub slider + play/pause. Lyrics text area (plain text or XML). "Prepare Lyrics…" button opens the structure editor. Engine + model selectors. "Analyze Audio" runs transcription and alignment.
- Top-right (2/3): Alignment results. Large display of the currently-active lyric line (synced to NAudio playback position via `CompositionTarget.Rendering` on every frame). ListBox of `LyricLineViewModel` items showing start/end timestamps, anchor checkbox, and text. "Save XML" preserves timing back into the XML format. "Export SRT / LRC" writes the subtitle file.
- Bottom: terminal log.

**The lyric XML format:** A custom XML schema that structures lyrics into sections:
```xml
<song title="..." artist="...">
  <section type="verse" id="verse1">
    <line>First lyric line</line>
    <line anchor="true" start="00:00:12.500" end="00:00:15.000">Second line (anchored)</line>
  </section>
  <section type="instrumental" duration="14.0"/>
  <section type="chorus" id="chorus1">
    <line>Chorus line one</line>
  </section>
  <section type="chorus" ref="chorus1"/>  <!-- repeats chorus1 -->
</song>
```
`LyricsXmlParser` reads this into `SongLyrics`. `LyricsXmlWriter` writes it back.

**The "Prepare Lyrics" dialog (`LyricsEditorWindow`):** Before alignment, the user structures raw lyric lines into sections. Three-column layout: left = unassigned source lines (multi-select with Shift/Ctrl-click), middle = section-type buttons (Verse, Pre-Chorus, Chorus, Bridge, Outro, Instrumental, etc.) plus "Auto-Detect VAD" button, right = assembled sections in order with move/delete controls and duration-hint fields for instrumentals. The "Auto-Detect VAD" button calls `InstrumentalDetectorService` which runs `vad_detect.py`. The result is serialized to the XML format and returned to the main window's `LyricsText` field.

**Transcription flow:** User clicks "Analyze Audio". `MainViewModel.AnalyzeAudioAsync` runs:
1. `FfprobeService` gets audio duration.
2. Based on `SelectedEngine`, runs one of: `OracleTranscriberService.TranscribeOracleAsync`, `WhisperXTranscriberService.TranscribeAsync`, `WhisperTranscriberService.TranscribeAsync`, or `VoskTranscriberService.TranscribeAsync`.
3. Each returns `IReadOnlyList<TimedWord>`.
4. `LyricsXmlParser.Parse(LyricsText)` parses the XML into `SongLyrics`.
5. `LyricsAligner.Align(songLyrics, words, options)` runs the N-W DP alignment.
6. Results populate the `Lines` `ObservableCollection<LyricLineViewModel>`.

**WhisperX specifically:** C# converts audio to 16 kHz mono WAV via FFmpeg. Passes it to `whisperx_driver.py` (a Python script embedded as a C# string constant in `WhisperXTranscriberService.DriverScript` and written to disk at install time). The driver loads WhisperX, runs faster-whisper for transcription, optionally uses supplied lyrics as an `initial_prompt` to bias the model, then runs wav2vec2 alignment for word-level timestamps. Returns JSON `{language, words:[{text, start, end, score}]}`. The C# side parses this into `TimedWord[]`.

**Oracle mode:** `OracleTranscriberService` launches all three engines in parallel with `Task.WhenAny`... actually with `Task.WhenAll` (waits for all), then picks the one with the most words, merges if multiple engines returned results. The merge adds high-confidence secondary words that don't duplicate primary words within a 200ms window.

**Export:** `ExportService` (currently in `LyricsLab/Services/`) writes SRT and LRC. This is a bug — it duplicates `SubtitleWriter` in Lab.Core and will drift. Should be replaced with direct calls to `SubtitleWriter`.

**NAudio playback:** `MainViewModel` holds `_player` (WaveOutEvent) and `_audioFile` (AudioFileReader). On `CompositionTarget.Rendering` (every WPF frame), it reads `_audioFile.CurrentTime` and updates `CurrentPosition`, which drives the active-line highlight in the ListBox. The scrub slider is two-way bound to `CurrentPositionSeconds`; `OnCurrentPositionSecondsChanged` seeks the audio file when the user drags the slider (with a 0.5s deadband to avoid feedback loops during playback).

### 6.2 VideoLab

**Purpose:** Render an MP4 visualizer video from an audio file and a static background image. The output is a full-length video with the image as background and animated spectrum bars overlaid.

**Window layout (`MainWindow.xaml`):** Four rows (auto:*:auto:300).
- Header: title + tool-status chip.
- Main: left control panel (420px fixed) + right live preview.
- GridSplitter.
- Terminal log.

**Left controls:** Audio browse, image browse (must be exactly 3840×2160), output path, `DarkColorPicker` for bar color, derived peak color swatch and opacity display, Render/Cancel buttons, progress bar, status + elapsed timer, "Open output folder" button.

**Right preview:** A `Viewbox` containing a 1920×1080 `Border` (simulating the output aspect ratio). Shows the selected image with a static 40-bar overlay (random heights, deterministic seed 42) at 0.5 opacity in the selected color. This is purely decorative — it shows the layout, not real audio data.

**Render flow (`VideoRenderViewModel.RenderAsync`):**
1. Snapshot the current UI state into a `VideoRenderJob` record (so mid-render UI changes don't affect the in-flight job).
2. Start a `Stopwatch` and a 1-second `DispatcherTimer` for the elapsed display.
3. Create a `CancellationTokenSource`. Expose a `CancelCommand` that calls `_cts.Cancel()`.
4. Call `VideoRenderService.RenderAsync(job, progress, onLog, ct)`.
5. Inside the service: validate file existence → probe image dimensions (must match job spec exactly) → probe audio duration → build FFmpeg filter graph (`SpectrumBarFilterBuilder.Build`) → run FFmpeg → parse progress → promote `.partial.mp4` to final path.
6. FFmpeg command: `-loop 1` over the image + audio input, `-filter_complex` with the spectrum graph, `-shortest`, h264_nvenc (GPU, preset p6/tune hq/vbr/cq19 — switched from software libx264 CRF 18 because a 4K60 software encode of this filter graph ran at well under 1fps), ALAC audio (lossless), `+faststart` for web streaming.

**Color derivation:** `PeakColor` is auto-derived as 30% darker than `SelectedColor` via `ColorMath.PeakFor`. It is used in the FFmpeg filter graph for peak bar rendering.

### 6.3 SongLab

**Purpose:** Master an audio track through a four-step pipeline: DSP filtering (SoX) → time-stretching (Rubberband R3) → loudness normalization (rsgain) → format encode (FFmpeg). Output: FLAC, WAV, or AIFF at 24-bit/48kHz.

**Window layout (`MainWindow.xaml`):** Same four-row shell.
- Header: title + tool-status chip + "Setup tools…" button.
- Main: two columns. Left = source/output/format/tempo controls + "Run mastering" button. Right = EQ filters (high-pass, low-pass, bass, treble sliders) + presets ComboBox + loudness normalization section.
- GridSplitter.
- Terminal log.

**EQ presets (`Models/EqPreset.cs`):** Record with Name, HighPassHz, LowPassHz, BassDb, TrebleDb. Five built-in: Custom/Flat, Subtle Warmth, Vocal/Speech Clarity, Rumble Removal, Club/Bass Boost. Selecting a preset applies its values to the sliders. Manually adjusting a slider after selecting a preset switches the preset back to "Custom/Flat" (via `CheckCustomPreset()`).

**Mastering pipeline (`MasteringPipeline.RunMasteringAsync`):**
1. Decode source to 24-bit 48kHz WAV (FFmpeg: `-acodec pcm_s24le -ar 48000`).
2. Apply SoX DSP filters (highpass, lowpass, bass, treble) if any are non-zero.
3. Rubberband R3 time-stretch if tempo ≠ 100% (`--tempo ratio --fine --ignore-clipping`).
4. Encode to final format (FFmpeg). Temp files are in a `Path.GetTempPath()` GUID-named directory, cleaned up in `finally`.
5. Optionally run rsgain loudness normalization on the output file (`rsgain custom -s i -l <lufs> -c p <file>`).

**Audio preview:** `MasteringViewModel` holds a `System.Windows.Media.MediaPlayer _player`. The play/pause toggle (▶/⏸ button) uses the WPF MediaPlayer for simple preview of the source file. This is not NAudio — it's the built-in WPF media pipeline.

### 6.4 ImageLab

**Purpose:** Non-destructive image editing with a live preview. Open an image, adjust crop/canvas/resize and color parameters, export the final result via ImageMagick.

**Window layout (`MainWindow.xaml`):** Same four-row shell.
- Left controls (380px): source browse, output browse, canvas/resize controls, crop fields (X/Y/W/H, toggled by checkbox), adjustment sliders (Brightness, Contrast, Gamma, Saturation, Chroma, Red, Green, Blue), "Export image" button.
- Right: live preview `Image` control.

**Preview debounce (`ImageEditViewModel`):** Any property change (slider move, crop value edit) calls `_previewTimer.Start()` which restarts a 220ms `DispatcherTimer`. When the timer fires it stops itself and calls `RenderPreviewAsync()`. This prevents a new ImageMagick process from launching on every slider tick. The preview renders to a fixed temp file `imagelab_preview.png`, then loads it as `BitmapImage` into `PreviewSource`.

**`ImageAdjustments` record:** Holds all edit state as a snapshot. `CurrentAdjustments()` assembles it from the ViewModel's properties on demand.

**`ImageMagickArgsBuilder`:** Translates `ImageAdjustments` into `magick <input> [operations...] <output>` arguments. Operations are ordered: crop → resize → brightness/contrast → gamma → saturation → chroma → RGB channel adjustments.

### 6.5 MediaLab.Hub

**Purpose:** Launcher. Shows a 2×2 tile grid with the four main apps. Clicking a tile starts that app's executable.

**ViewModel (`ViewModels/MainViewModel.cs`):** Holds a fixed `ObservableCollection<AppTile>` of four tiles. `LaunchAppCommand(AppTile tile)` resolves the exe path as `Path.Combine(AppContext.BaseDirectory, "..", tile.ExeName, tile.ExeName + ".exe")` (one level up from Hub's own `Build\MediaLab.Hub\` folder, into the sibling `Build\<AppName>\` folder). Calls `Process.Start()`. Sets `StatusMessage` if the exe is not found.

---

## 7. How Components Interoperate

### 7.1 Data flow in LyricsLab

```
User picks audio file
    → NAudio loads for playback
    → FfprobeService queries duration
    
User "Prepares Lyrics" (opens LyricsEditorWindow)
    → User assigns raw lines to sections
    → Optionally: InstrumentalDetectorService → vad_detect.py → JSON gaps
    → LyricsXmlWriter serializes to XML string
    → XML string returned to MainWindow.LyricsText
    
User clicks "Analyze Audio"
    → LyricsXmlParser.Parse(LyricsText) → SongLyrics
    → Engine transcribes audio → IReadOnlyList<TimedWord>
        WhisperX path: FFmpeg converts to WAV → whisperx_driver.py → JSON → TimedWord[]
        Whisper path: Whisper CLI invoked → JSON → TimedWord[]
        Vosk path: Vosk CLI invoked → JSON → TimedWord[]
        Oracle path: all three in parallel → TranscriptMerger.MergeTranscripts()
    → LyricsAligner.Align(SongLyrics, TimedWord[]) → AlignedLine[]
    → Lines ObservableCollection<LyricLineViewModel> populated
    
User adjusts timings, marks anchors
    → LyricLineViewModel.IsAnchor / StartTime / EndTime edited
    
User "Save XML"
    → Lines mapped back into LyricLineData with updated anchors/times
    → LyricsXmlWriter.Write(updatedSongLyrics) → file
    
User "Export"
    → ExportService.ExportToSrt() or ExportToLrc() → file
    (BUG: should be SubtitleWriter.ToSrt() / ToLrc() instead)
```

### 7.2 Data flow in VideoLab

```
User picks audio + image + output path + color
    → VideoRenderViewModel snapshots → VideoRenderJob record
    → VideoRenderService.RenderAsync(job, progress, onLog, ct)
        → ImageProbeService validates image is exactly 3840×2160
        → FfprobeService gets audio duration (for progress %)
        → SpectrumBarFilterBuilder.Build(job) → FFmpeg filter graph string
        → ProcessRunner runs FFmpeg with arguments
        → FfmpegProgressParser parses stdout → IProgress<VideoRenderProgress>
        → .partial.mp4 promoted to final path on success
```

### 7.3 Data flow in SongLab

```
User picks source + output + format + EQ settings + tempo
    → MasteringViewModel.RunAsync()
        → MasteringPipeline.RunMasteringAsync(...)
            → FFmpeg: decode to temp WAV
            → SoX: apply EQ filters to temp WAV
            → Rubberband: time-stretch temp WAV
            → FFmpeg: encode stretched WAV to output file
            → rsgain: normalize loudness on output file (optional)
```

### 7.4 Data flow in ImageLab

```
User picks source image
    → ImageProbeService.GetDimensionsAsync() → "W × H" display
    
User adjusts sliders (any property change)
    → 220ms debounce timer fires
    → CurrentAdjustments() snapshots ImageAdjustments record
    → ImageMagickArgsBuilder.Build(adjustments, sourcePath, previewPath)
    → ImageOpsService runs magick → imagelab_preview.png
    → BitmapImage loaded from file → PreviewSource updated
    
User "Export image"
    → ImageMagickArgsBuilder.Build(adjustments, sourcePath, outputPath)
    → ImageOpsService runs magick → output file
```

### 7.5 Key shared contracts

All apps share these contracts from Lab.Core/Lab.UI:

- `IProcessRunner` — the only way any service invokes an external process.
- `ToolLocator` — the only way any code finds an executable.
- `TerminalViewModel` — the only way log output reaches the UI.
- `DarkTheme.xaml` — the only way colors and styles are defined.
- `IFilePickerService` — the only way ViewModels open file dialogs.

---

## 8. The Architecture Problem: Layer Violations

**The current state is brittle for one reason:** domain logic leaked into `Lab.Core` when it belongs with each app.

`Lab.Core` currently contains the lyrics alignment engine (`LyricsAligner`, `OracleTranscriberService`, all the transcription services), the video render engine (`VideoRenderService`, `SpectrumBarFilterBuilder`), the mastering pipeline (`MasteringPipeline`), and the image processing layer (`ImageOpsService`, `ImageMagickArgsBuilder`). None of these belong in a shared library. `LyricsAligner` is not used by VideoLab. `VideoRenderService` is not used by SongLab. They are in Lab.Core purely by historical accident.

**The consequence:** A compilation error in LyricsLab's transcription services blocks VideoLab from building. A breaking change in `MasteringPipeline` requires rebuilding and retesting the entire suite. The solution cannot be split into independent repositories without major surgery.

**The correct rule:**
- `Lab.Core` → infrastructure only: `ProcessRunner`, `ToolLocator`, `DependencyVerifier`, `BoundedLogBuffer`, `ZipInstallerService`, `FfmpegInstallerService`. These are genuinely cross-cutting, stable, and boring.
- `Lab.UI` → style + UI infrastructure only: `DarkTheme.xaml`, converters, `TerminalViewModel`, `ToolStatusViewModel`, `DarkColorPicker`, `IFilePickerService`, `FilePickerService`. Zero business logic.
- Each app owns its domain logic entirely. `LyricsLab` owns `LyricsAligner`, `SubtitleWriter`, all transcription services, the XML format. `VideoLab` owns `VideoRenderService`, `SpectrumBarFilterBuilder`, `ColorMath`. `SongLab` owns `MasteringPipeline`. `ImageLab` owns `ImageOpsService`, `ImageMagickArgsBuilder`, `ImageAdjustments`.

After this refactor, any app can be extracted to its own repository by taking its folder, the small stable `Lab.Core` infrastructure folder, and the `Lab.UI` style folder. No domain logic crosses app boundaries.

---

## 9. Bugs to Fix (Ordered by Severity)

### 9.1 CRITICAL — SongLab: Manual EQ slider adjustments are immediately overwritten

**File:** `SongLab/UI/ViewModels/MasteringViewModel.cs`, method `OnSelectedPresetChanged`

**Root cause:** `CheckCustomPreset()` is called by every slider's `partial void On*Changed` hook. When the user moves a slider that deviates from the current preset's values, `CheckCustomPreset()` sets `SelectedPreset = Presets[0]` ("Custom/Flat"). This triggers `OnSelectedPresetChanged`. The second `if` block in `OnSelectedPresetChanged` has the condition `value.Name != "Custom / Flat" || (value.HighPassHz == 0 && ...)` — which is true when switching to "Custom/Flat" with all-zero values, so it overwrites all four sliders with 0. The user's adjustment is immediately undone.

**Fix:** In `OnSelectedPresetChanged`, only apply values when switching *to* a real preset. When switching to Custom/Flat, do nothing:
```csharp
partial void OnSelectedPresetChanged(Models.EqPreset value)
{
    if (value.Name == "Custom / Flat") return;
    HighPassHz = value.HighPassHz;
    LowPassHz  = value.LowPassHz;
    BassDb     = value.BassDb;
    TrebleDb   = value.TrebleDb;
}
```
Remove the existing dead first `if` block and the broken second `if` block entirely.

### 9.2 CRITICAL — SongLab: Auto-generated output path is always `.flac` regardless of selected format

**File:** `SongLab/UI/ViewModels/MasteringViewModel.cs`, method `UpdateOutputPath`

**Root cause:** `UpdateOutputPath()` hardcodes `.flac` extension. When the user selects "WAV (24-bit)" or "AIFF (24-bit)" without manually editing the output path, FFmpeg receives a `.flac`-extension path while being told to encode PCM audio — FFmpeg selects its muxer by file extension, so WAV codec in a FLAC container fails or produces a corrupt file.

Also: when tempo is 100%, the suffix is `"0"`, producing `song_0.flac` instead of `song_master.flac`.

**Fix:**
```csharp
private void UpdateOutputPath()
{
    if (string.IsNullOrWhiteSpace(SourcePath)) return;
    var dir  = Path.GetDirectoryName(SourcePath) ?? string.Empty;
    var stem = Path.GetFileNameWithoutExtension(SourcePath);
    var ext  = SelectedFormat.Contains("AIFF") ? ".aiff"
             : SelectedFormat.Contains("WAV")  ? ".wav"
             : ".flac";
    int tempoDiff = (int)Math.Round(TempoPercent - 100);
    var suffix = tempoDiff == 0 ? "_master" : $"_{(tempoDiff > 0 ? "+" : "")}{tempoDiff}pct";
    OutputPath = Path.Combine(dir, $"{stem}{suffix}{ext}");
}
```
Also add `partial void OnSelectedFormatChanged(string value) => UpdateOutputPath();` so the path updates when the format ComboBox changes.

### 9.3 HIGH — LyricsLab: No cancellation on transcription

**File:** `LyricsLab/ViewModels/MainViewModel.cs`

**Root cause:** `AnalyzeAudioAsync` can run for many minutes (WhisperX on CPU with a large model). There is no `CancellationTokenSource`, no cancel command, and no cancel button. The user is stuck waiting with no recourse.

**Fix:** Add a `CancellationTokenSource? _analysisCts` field. Add `[RelayCommand] private void CancelAnalysis()` that cancels it. Pass the token through all engine calls. Add a Cancel button next to "Analyze Audio" in `MainWindow.xaml`, bound to `CancelAnalysisCommand`, visible only when `IsAnalyzing` is true.

### 9.4 HIGH — SongLab: No cancellation on mastering pipeline

**File:** `SongLab/UI/ViewModels/MasteringViewModel.cs`

**Root cause:** `RunAsync()` passes `cancellationToken: default` to `RunMasteringAsync`. The pipeline fully supports cancellation (all `RunOrThrowAsync` calls pass `ct`) but the ViewModel never creates a token.

**Fix:** Same pattern as 9.3. The pipeline's `MasteringPipeline` already has the `CancellationToken` parameter — just wire it up from the ViewModel.

### 9.5 HIGH — LyricsLab: Oracle mode ignores the model ComboBox

**File:** `Lab.Core/Lyrics/OracleTranscriberService.cs`, constructor

**Root cause:** WhisperX is hardcoded to `"small"` regardless of `SelectedModel` in the ViewModel.

**Fix:** Add a `model` parameter to `TranscribeOracleAsync` and pass `SelectedModel` from `MainViewModel.AnalyzeAudioAsync`:
```csharp
public async Task<IReadOnlyList<TimedWord>> TranscribeOracleAsync(
    string audioPath, string? lyricsHint, string model, Action<string> onLog, CancellationToken ct = default)
```

### 9.6 HIGH — LyricsLab: NAudio resources and rendering event never disposed

**File:** `LyricsLab/ViewModels/MainViewModel.cs`

**Root cause:** `_player` (`WaveOutEvent`) and `_audioFile` (`AudioFileReader`) are IDisposable. `CompositionTarget.Rendering` is subscribed in the constructor but never unsubscribed. These leak on window close.

**Fix:** Implement `IDisposable` on `MainViewModel`. In `Dispose()`: `CompositionTarget.Rendering -= OnRendering; _player?.Dispose(); _audioFile?.Dispose();`. Call from `MainWindow.OnClosed`.

### 9.7 MEDIUM — LyricsLab: ExportService duplicates SubtitleWriter

**File:** `LyricsLab/Services/ExportService.cs`

**Root cause:** Reimplements SRT and LRC serialization. `SubtitleWriter` in Lab.Core already does this (plus five more formats). Any fix to `SubtitleWriter` won't apply to LyricsLab's export.

**Fix:** Delete `ExportService.cs`. In `MainViewModel.Export`, map `LyricLineViewModel` items to `AlignedLine` records and call `SubtitleWriter.ToSrt()` / `SubtitleWriter.ToLrc()` directly. While doing this, expose all seven subtitle formats in the export dialog (SRT, LRC, VTT, ASS, SBV, SUB, JSON).

### 9.8 MEDIUM — SongLab: Format detection via string.Contains on display name

**File:** `Lab.Core/Media/MasteringPipeline.cs`

**Root cause:** `if (format.Contains("FLAC"))` etc. couples pipeline code to UI display strings.

**Fix:** Introduce a `DeliveryFormat` enum (`Flac`, `Wav`, `Aiff`) in Lab.Core or SongLab. Pass it instead of the string. Let `MasteringViewModel` convert `SelectedFormat` to the enum at the call site.

### 9.9 MEDIUM — SongLab: rsgain failure after valid encode shows "Mastering failed"

**File:** `Lab.Core/Media/MasteringPipeline.cs`

**Root cause:** If rsgain fails after encoding succeeds, an exception propagates and the user sees "Mastering failed" even though the output file is complete and valid. The encoded file is just untagged for loudness.

**Fix:** Catch rsgain failures as non-fatal warnings rather than exceptions. Log the warning via `onStatus` and continue.

### 9.10 MEDIUM — SongLab: Mastering tool output not streamed to terminal

**File:** `Lab.Core/Media/MasteringPipeline.cs`, `RunOrThrowAsync`

**Root cause:** `_runner.RunAsync(toolPath, args, cancellationToken: ct)` — no `onOutputLine`/`onErrorLine` callbacks. SoX, Rubberband, and rsgain output is silently swallowed during a run.

**Fix:** Accept `Action<string>? onLog` in `RunOrThrowAsync` and pass it through as both `onOutputLine` and `onErrorLine`.

### 9.11 LOW — VideoLab: VideoRenderService takes concrete ProcessRunner

**File:** `Lab.Core/Media/VideoRenderService.cs`

**Root cause:** Constructor takes `ProcessRunner` (concrete) instead of `IProcessRunner`. Inconsistent with every other service in the codebase and untestable without FFmpeg.

**Fix:** Change field and constructor parameter to `IProcessRunner`.

### 9.12 LOW — LyricsAligner: anchorLineIndices.Contains is O(n) in a loop

**File:** `Lab.Core/Lyrics/LyricsAligner.cs`

**Fix:** `var anchorSet = new HashSet<int>(anchorLineIndices);` and use `anchorSet.Contains(v)`.

### 9.13 LOW — TerminalViewModel: DispatcherTimer never stopped

**File:** `Lab.UI/ViewModels/TerminalViewModel.cs`

**Fix:** Implement `IDisposable`, stop the timer in `Dispose()`. Have consuming ViewModels call it on teardown.

---

## 10. LyricsLab V3 — The Planned Replacement

A V3 redesign is planned. Key decisions are already made:

**Four-stage workflow** (replaces the current single-window design):
1. **Setup & Import** — tool checks, audio file picker, language/model selection
2. **Structure Editor** — the existing `LyricsEditorWindow` promoted to a full stage
3. **Transcribe & Align** — runs transcription + alignment, shows results, allows manual correction
4. **Export** — subtitle format selection and file write

**VAD-based instrumental detection** — already implemented in `InstrumentalDetectorService` + `vad_detect.py`. Confirmed approach: use pyannote VAD (already in the WhisperX venv) rather than FFmpeg silence detection (which misses loud instrumentals).

**Carry-forward reuse:**
- `LyricsXmlParser`, `LyricsXmlWriter` — keep as-is
- `SongLyrics`, `LyricSection`, `LyricLineData` — keep as-is (already have `IsInstrumental`, `DurationHint`, `IsReference`)
- `LyricsAligner` — keep as-is (already skips instrumentals, already supports anchors)
- `SubtitleWriter` — keep as-is (SRT, LRC, VTT, ASS, SBV, MicroDVD, JSON)
- `InstrumentalDetectorService` + `vad_detect.py` — keep as-is
- `OracleTranscriberService` — keep (fix model parameter first, per bug 9.5)

**What changes:** The UI structure. The existing `LyricsEditorWindow` becomes Stage 2. A new `MainWindow` hosts the four-panel wizard flow instead of the current combined layout. The NAudio playback code from `MainViewModel` moves to Stage 3 where it belongs.

**Implementation phases:**
1. Scaffold the new project structure (or rename the existing LyricsLab)
2. Build the four-panel wizard shell
3. Wire `InstrumentalDetectorService` to the Stage 2 Auto-Detect button (already done in current code)
4. Wire `OracleTranscriberService` in Stage 3 (currently done — just move it)
5. Wire `SubtitleWriter` in Stage 4 (fix the ExportService duplication first)

---

## 11. What Has Been Done vs What Is Pending

### Done and working
- Lab.Core infrastructure (ProcessRunner, ToolLocator, DependencyVerifier, FfmpegInstallerService)
- Lab.UI (DarkTheme, all converters, TerminalViewModel, ToolStatusViewModel, DarkColorPicker)
- VideoLab: fully working, cancel supported, progress working, correct tool abstraction
- ImageLab: fully working, debounced preview, export working
- LyricsAligner: algorithm correct, anchor support works
- LyricsXmlParser / LyricsXmlWriter: solid
- SubtitleWriter: all seven formats implemented correctly
- InstrumentalDetectorService + vad_detect.py: implemented and working
- WhisperXTranscriberService + embedded driver script: working
- OracleTranscriberService: working (just needs model parameter fix)
- MediaLab.Hub: working
- MasteringPipeline: steps correct, tool invocations correct (needs cancel wiring and non-fatal rsgain)

### Broken / not yet done
- SongLab EQ preset + slider interaction (bug 9.1 — CRITICAL, makes EQ non-functional)
- SongLab output path format mismatch (bug 9.2 — CRITICAL)
- Cancel commands for LyricsLab transcription and SongLab mastering (bugs 9.3, 9.4)
- Oracle model passthrough (bug 9.5)
- LyricsLab NAudio disposal (bug 9.6)
- ExportService deduplication (bug 9.7)
- MasteringPipeline format enum (bug 9.8)
- rsgain non-fatal (bug 9.9)
- Mastering tool output streaming (bug 9.10)
- VideoRenderService interface fix (bug 9.11)
- HashSet in LyricsAligner (bug 9.12)
- TerminalViewModel disposal (bug 9.13)
- Architecture refactor (domain logic out of Lab.Core into each app)
- LyricsLab V3 UI redesign

---

## 12. Summary: What to Do Next

If taking on this codebase, the priority order is:

1. **Fix bugs 9.1 and 9.2** — SongLab is currently non-functional for EQ editing and for non-FLAC output. Two small fixes in `MasteringViewModel.cs`.
2. **Fix bugs 9.3 and 9.4** — Add cancel commands. LyricsLab transcription can hang the session permanently.
3. **Fix bug 9.5** — Pass model to Oracle. The UI model selector is currently a no-op.
4. **Fix bug 9.7** — Delete ExportService, call SubtitleWriter directly, expose all seven formats.
5. **Fix bug 9.6** — Wire disposal.
6. **Fix remaining medium/low bugs** (9.8–9.13) — These are small, clean them up together.
7. **Architecture refactor** — Move domain namespaces from Lab.Core into their respective app projects. Lab.Core becomes infrastructure-only. Each domain namespace (`Lab.Core.Lyrics`, `Lab.Core.Media` for VideoLab types, `Lab.Core.Imaging`) moves into `LyricsLab`, `VideoLab`, `ImageLab` respectively. SongLab's `MasteringPipeline` already lives in Lab.Core/Media — move it into `SongLab` directly. Update all `using` statements and `<ProjectReference>` entries accordingly.
8. **LyricsLab V3** — Once the current LyricsLab is stable and the architecture is clean, redesign the UI as a four-stage wizard.
