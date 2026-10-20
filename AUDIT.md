# MediaLab — Full Codebase Audit

This document is a complete technical audit of the MediaLab solution, intended to give a new session full context for any implementation or design work.

---

## Solution Overview

**MediaLab** is a personal desktop media-utility suite built in C# / WPF on .NET 10.0 (Windows only). It lives at `D:\GitHub\MediaLab` as a `.slnx` solution. The suite consists of four standalone GUI applications, one hub launcher, two shared libraries, and one test project. All apps share a consistent dark theme, a terminal/log panel, and a tool-status indicator in the header.

**Technology stack:**
- Framework: .NET 10.0-windows (WPF)
- MVVM: CommunityToolkit.Mvvm 8.4.2 (`[ObservableProperty]`, `[RelayCommand]`)
- C# 14, nullable enabled, implicit usings, x64 target
- Build output: `Build\<AppName>\` flat folder (not nested bin/)
- External CLI tools: FFmpeg/FFprobe, ImageMagick (`magick`), SoX, Rubberband (`rubberband`), rsgain, Whisper (OpenAI), WhisperX, Vosk
- Python integration: WhisperX runs inside its own venv; a custom `vad_detect.py` script for Voice Activity Detection

**Projects in build order (`MediaLab.slnx`):**
1. Lab.Core
2. Lab.UI
3. LyricsLab
4. VideoLab
5. SongLab
6. ImageLab
7. MediaLab.Hub
8. MediaLab.Tests

---

## Project 1 — Lab.Core (shared library, no UI)

**Target:** `net10.0` (not Windows-specific)  
**NuGet:** `Vosk 0.3.38`  
**References:** none

Lab.Core holds all business logic. It never touches WPF.

### Lyrics/ (core lyric and transcription logic)

| File | Purpose |
|------|---------|
| `SongLyrics.cs` | Data model. Records: `SongLyrics` (list of `LyricSection`), `LyricSection` (type string, `IsInstrumental`, `IsReference`, `DurationHint`, list of `LyricLineData`), `LyricLineData` (text, IsAnchor, DistanceNext, optional Start/End TimeSpan). |
| `LyricsXmlParser.cs` | Static parser. Reads the custom XML lyric format into `SongLyrics`. |
| `LyricsXmlWriter.cs` | Static serializer. Writes `SongLyrics` back to XML preserving anchors, distances, and timing. |
| `LyricsAligner.cs` | Static aligner. Implements Needleman-Wunsch global DP to match official lyric lines against a `TimedWord[]` transcript. Token-level fuzzy similarity (0.7 threshold). Skips instrumental sections automatically. Returns `AlignedLine[]` with Start, End, Text, Confidence. |
| `TimedWord.cs` | Record struct: `Text`, `Start` (TimeSpan), `End` (TimeSpan), `Confidence` (float 0–1). |
| `TimingIssue.cs` | Discriminated union records for detected issues: Overlap, Gap, LongCue, ShortCue, etc. |
| `TimingIssueDetector.cs` | Static analyzer. Scans an `AlignedLine[]` and returns `TimingIssue[]`. |
| `SubtitleWriter.cs` | Static converter. Produces SRT and LRC files from `AlignedLine[]`. |
| `SpeechModelInfo.cs` | Record describing an available speech model: engine name, model id, size label, installed status. |
| `TranscriptMerger.cs` | Static merger. Combines results from multiple engines using confidence weighting. |
| `OracleTranscriberService.cs` | Runs WhisperX + Whisper.cpp + Vosk in parallel via `Task.WhenAny`. Returns the first reliable result. Accepts a `ToolLocator` in its constructor. |
| `WhisperXTranscriberService.cs` | Wraps the WhisperX Python venv. Invokes `whisperx_driver.py` via `ProcessRunner`. Supports alignment mode. Parses JSON output via `WhisperCppJsonParser`. |
| `WhisperTranscriberService.cs` | Wraps the Whisper CLI (`whisper.exe` or equivalent). Locates model file, invokes process, parses JSON. |
| `VoskTranscriberService.cs` | Wraps Vosk CLI / offline model directory. Fallback transcriber. |
| `WhisperCppJsonParser.cs` | Parses the JSON word-level output shared by Whisper and WhisperX into `TimedWord[]`. |
| `WhisperInstallerService.cs` | Downloads Whisper models from HuggingFace / AlphaCephei. Manages install path and version checks. |

### Media/ (video, audio, image pipeline)

| File | Purpose |
|------|---------|
| `VideoRenderJob.cs` | Immutable record. Defines one render task: AudioPath, ImagePath, OutputPath, BarColor, PeakColor, Width, Height, FrameRate, BarCount, Opacity. |
| `VideoRenderProgress.cs` | Record reporting render progress: percent, elapsed, phase string. |
| `VideoRenderResult.cs` | Record: OutputPath on success, error message on failure. |
| `VideoRenderService.cs` | Orchestrates FFmpeg render. Validates inputs, probes duration/dimensions via FfprobeService/ImageProbeService, builds filter graph via `SpectrumBarFilterBuilder`, invokes FFmpeg, parses progress via `FfmpegProgressParser`, promotes `.partial.mp4` to final path on success. |
| `SpectrumBarFilterBuilder.cs` | Generates FFmpeg `lavfi` filter-graph string for the spectrum-bar visualizer: 30 Hz highpass, logarithmic scaling, multi-bar overlap/averaging, with separate peak-color overlay. |
| `FfprobeService.cs` | Invokes `ffprobe` to return media duration as `TimeSpan?`. |
| `ImageProbeService.cs` | Invokes `ffprobe` to return image dimensions as `(int W, int H)?`. |
| `FfmpegProgressParser.cs` | Stateful line parser. Extracts `out_time` from FFmpeg stderr to produce a 0–100 percent value. |
| `MasteringPipeline.cs` | Audio mastering orchestrator. Steps: decode to PCM (FFmpeg) → SoX DSP (EQ/compression) → Rubberband R3 time-stretch → rsgain loudness normalization → encode (FFmpeg, to FLAC/WAV/AIFF). |
| `OutputPathBuilder.cs` | Utilities: `ForAudio()` derives a default output filename; `Partial()` appends `.partial` suffix. |
| `ColorMath.cs` | RGB utilities: parse hex strings, convert to FFmpeg-format hex, compute 30%-darker peak color. |
| `InstrumentalDetectorService.cs` | Detects instrumental/non-vocal gaps. Invokes `vad_detect.py` (Python) via `ProcessRunner`, parses its JSON output, returns non-vocal segments longer than a configured threshold. |

### Imaging/

| File | Purpose |
|------|---------|
| `ImageAdjustments.cs` | Immutable record. Fields: CropEnabled, CropX/Y/W/H, TargetWidth, TargetHeight, ResizeMode, Brightness, Contrast, Gamma, Saturation, Chroma, Red, Green, Blue. All int/double. |
| `ImageMagickArgsBuilder.cs` | Translates an `ImageAdjustments` record into a `magick` command-line argument string. |
| `ImageOpsService.cs` | Runs `magick` via `ProcessRunner` with the built args. Returns success/error. |

### Processes/

| File | Purpose |
|------|---------|
| `IProcessRunner.cs` | Interface. `RunAsync(exe, args, workingDir, onStdOut, onStdErr, ct)` → `ProcessResult`. |
| `ProcessRunner.cs` | Concrete implementation. Uses `System.Diagnostics.Process`. Handles encoding, line buffering, cancellation token. |
| `ProcessResult.cs` | Record: `ExitCode`, `StdOut`, `StdErr`. |

### Tools/

| File | Purpose |
|------|---------|
| `ToolLocator.cs` | Multi-stage finder. Checks: (1) configured path, (2) PATH env, (3) portable `tools/` subdirectory, (4) legacy `D:\cli\tools`. Returns absolute path or null. |
| `ToolDefaults.cs` | Constants for tool subdirectory names and the legacy dev root. |
| `DependencyVerifier.cs` | Scans for all required tools and returns `DependencyStatus[]` (name, found, path). Used to populate the tool-status indicator in each app's header. |
| `FfmpegInstallerService.cs` | Downloads FFmpeg release archive from gyan.dev, extracts to the portable tools directory. |
| `ZipInstallerService.cs` | Generic async zip downloader and extractor. |

### Logging/

| File | Purpose |
|------|---------|
| `BoundedLogBuffer.cs` | Thread-safe circular buffer. Caps log lines at a configured size. Supports draining oldest entries. |

### Scripts/

| File | Purpose |
|------|---------|
| `vad_detect.py` | Python script. Loads audio at 16 kHz mono via `whisperx.load_audio()`, runs pyannote VAD (`whisperx.load_vad_model()`), inverts speech segments to produce non-vocal gaps, outputs JSON `{ "non_vocal": [{start, end, duration}], "vocal": [...] }`. Falls back gracefully when `libtorchcodec_core7.dll` is absent (uses torchaudio/soundfile instead). |

---

## Project 2 — Lab.UI (shared WPF library)

**Target:** `net10.0-windows`  
**NuGet:** `CommunityToolkit.Mvvm 8.4.2`  
**References:** Lab.Core

### Controls/

| File | Purpose |
|------|---------|
| `DarkColorPicker.xaml` + `.cs` | Custom HSV color picker control. Has a gradient hue bar, saturation/value canvas, recent-colors strip, and hex input. Exposes `SelectedColor` as `RgbColor` (Lab.Core type). |
| `AutoScrollBehavior.cs` | Attached behavior that scrolls a ListBox to its selected item automatically. |
| `ColorConversions.cs` | Converts `System.Windows.Media.Color` ↔ HSV tuple. |

### Converters/

| File | Purpose |
|------|---------|
| `BoolToVisibilityConverter.cs` | `bool → Visibility`. Supports `ConverterParameter=invert`. |
| `InverseBoolConverter.cs` | `bool → !bool`. |
| `ColorToBrushConverter.cs` | `RgbColor → SolidColorBrush`. |
| `NullOrEmptyToVisibilityConverter.cs` | `string? or ICollection? → Visibility`. |

### Services/

| File | Purpose |
|------|---------|
| `IFilePickerService.cs` | Interface: `PickOpenFile(filter)`, `PickSaveFile(filter, defaultExt)`. |
| `FilePickerService.cs` | WPF `OpenFileDialog`/`SaveFileDialog` implementation. |

### ViewModels/

| File | Purpose |
|------|---------|
| `TerminalViewModel.cs` | Observable log panel. Thread-safe. Accepts `Append(string)` calls from any thread. Batches updates via `DispatcherTimer` to avoid saturating the UI thread. Exposes `Lines` (ObservableCollection<string>) and `CopyCommand` / `ClearCommand`. |
| `ToolStatusViewModel.cs` | Wraps `DependencyVerifier`. Exposes `AllPresent` (bool), `StatusText`, and a `RefreshCommand`. Used in each app's header status chip. |

### Themes/

| File | Purpose |
|------|---------|
| `DarkTheme.xaml` | Shared resource dictionary. Defines all color brushes (`WindowBackgroundBrush`, `PanelBackgroundBrush`, `ControlBackgroundBrush`, `AccentBrush`, `ForegroundBrush`, `SubtleForegroundBrush`, `BorderBrush`, etc.) and reusable styles (`HeaderTextBlockStyle`, `SubtleTextBlockStyle`, `LogTextStyle`, `AccentButtonStyle`). |

---

## Project 3 — LyricsLab (GUI application)

**Target:** `net10.0-windows`, WinExe  
**NuGet:** `CommunityToolkit.Mvvm 8.4.2`, `NAudio 2.3.0`  
**References:** Lab.Core, Lab.UI  
**Window title at runtime:** "LyricsLab v2 - Timing Editor" (this is the renamed LyricsLabV2 project)

### What it does

LyricsLab is an AI-powered lyrics timing and subtitle-generation tool. The user loads an audio file, pastes or loads lyrics (as plain text or the custom XML format), runs transcription, gets word-level timings aligned to the lyrics, can manually mark anchor lines and adjust timings, then exports to SRT or LRC.

### UI layout (`MainWindow.xaml`)

Three-row layout (2:auto:1 proportions):

**Top row — two columns:**
- *Left column* (1/3 width): Audio setup panel
  - "Pick Audio" button + path display
  - Play/Pause button, current-position display, scrub slider, total-duration display (NAudio playback)
  - "2. Lyrics" header with "Prepare Lyrics…" (opens LyricsEditorWindow), "Load File…", "Setup Dependencies…" buttons
  - Multi-line TextBox for raw lyrics text (plain text or XML)
  - Bottom bar: "Analyze Audio (Draft Timing)" button, engine ComboBox (`Auto (Oracle)` / `WhisperX` / `Whisper` / `Vosk`), model ComboBox (`tiny` / `base` / `small` / `medium` / `large-v3`)
- *Right column* (2/3 width): Alignment results panel
  - "3. Alignment Results" header
  - Large blue-text display of the currently active lyric line (synced to playback position)
  - ListBox of `LyricLineViewModel` items, each showing: Anchor checkbox, Start time (mm:ss.fff), End time, line text. Active line highlighted in blue. Mouse-over hover highlight.
  - Footer: status text, "Save XML…" button, "Export SRT / LRC…" button

**Middle row:** Draggable GridSplitter

**Bottom row:** Read-only Consolas terminal TextBox (FFmpeg/Whisper output)

### ViewModels

**`ViewModels/MainViewModel.cs`**  
The primary orchestration ViewModel. Properties:
- `AudioFilePath`, `LyricsText`, `TotalAudioDuration`, `CurrentPosition`, `IsPlaying`, `CurrentPositionSeconds`, `TotalAudioDurationSeconds`
- `ActiveLyricText` — text of the currently-playing lyric line
- `AvailableEngines`, `SelectedEngine`, `AvailableModels`, `SelectedModel`
- `StatusText`
- `Terminal` — shared `TerminalViewModel`
- `Lines` — `ObservableCollection<LyricLineViewModel>`

Commands:
- `PickAudioCommand` — opens file dialog, loads audio via NAudio `AudioFileReader` + `WaveOutEvent`
- `PlayPauseCommand` — toggles NAudio playback
- `PickLyricsCommand` — opens file dialog, reads XML/text into `LyricsText`
- `PrepareLyricsCommand` — opens `LyricsEditorWindow` (modal dialog); writes result back to `LyricsText`
- `SetupDependenciesCommand` — opens `SetupWindow`
- `AnalyzeAudioCommand` (async) — runs transcription via selected engine, then `LyricsAligner.Align()`, populates `Lines`
- `SaveXmlCommand` — re-builds `SongLyrics` from `Lines` (preserving anchors/times), serializes via `LyricsXmlWriter`
- `ExportCommand` — exports to SRT or LRC via `ExportService`

On each WPF render tick (`CompositionTarget.Rendering`), updates `CurrentPosition` from NAudio and sets `IsActive` on the matching `LyricLineViewModel`.

**`ViewModels/LyricLineViewModel.cs`**  
Observable item for one aligned lyric line:
- `Text`, `StartTime` (TimeSpan), `EndTime` (TimeSpan), `IsAnchor` (bool), `IsActive` (bool for playback highlight), `DistanceNext` (double, spacing hint for XML)

### Dialog Windows

**`UI/Views/LyricsEditorWindow.xaml` + `.cs`**  
Modal dialog for structuring raw lyric lines into named sections before alignment.

Layout — three columns:
- *Left* (2*): "Source lyrics" ListBox — all unassigned `SourceLine` items. Supports Shift-click range, Ctrl-click individual selection, drag-selection. Add-custom-line TextBox+button at bottom.
- *Middle* (Auto): Claim-button panel. Lists vocal section-type buttons (Verse, Pre-Chorus, Chorus, Bridge, Outro, Other) then a separator, then "Auto-Detect (VAD)" button and "+ Instrumental (Manual)" button.
- *Right* (1.6*): "Sections (in song order)" ItemsControl. Each `EditorSection` card shows: section type + line count summary, first-line preview (vocal), duration-hint TextBox (instrumental). Per-card buttons: Copy, + Instr, ↑, ↓, ✕.

Footer: "Copy XML", "Save As…", "Apply" (IsDefault), "Cancel" (IsCancel) buttons + remaining-count warning.

ViewModel (`UI/ViewModels/LyricsEditorViewModel.cs`):
- `RemainingLines` — lines not yet assigned
- `Sections` — `ObservableCollection<EditorSection>` being built
- `ClaimAsCommand(sectionTypeCode)` — moves selected source lines into a new section of the given type
- `AddInstrumentalCommand` — inserts a blank instrumental `EditorSection`
- `AutoDetectInstrumentalsCommand` — calls `InstrumentalDetectorService`, inserts instrumental sections at detected positions
- `DuplicateSectionCommand`, `InsertInstrumentalAfterCommand`, `MoveUpCommand`, `MoveDownCommand`, `RemoveSectionCommand`
- `AddLineCommand` — adds a custom line typed in `NewLineText` to `RemainingLines`
- Output: serialized XML string via `LyricsXmlWriter`, returned as `editor.Result`

**`UI/Views/SetupWindow.xaml` + `.cs`** / **`UI/ViewModels/SetupViewModel.cs`**  
Dependency setup dialog. Lists speech models (Whisper, WhisperX, Vosk) with name/size/status. Download/install buttons. Progress display. Uses `WhisperInstallerService`.

### Services

| File | Purpose |
|------|---------|
| `Services/ExportService.cs` | Static methods `ExportToSrt(lines, path)` and `ExportToLrc(lines, path)`. Delegates to `Lab.Core.Lyrics.SubtitleWriter`. |
| `Services/AlignmentService.cs` | Helper wrapping `LyricsAligner.Align()`. |
| `Services/AudioAwareAlignmentService.cs` | Extends alignment with audio-duration-based heuristics (clips lines that would extend past the end of the file). |

---

## Project 4 — VideoLab (GUI application)

**Target:** `net10.0-windows`, WinExe  
**NuGet:** `CommunityToolkit.Mvvm 8.4.2`  
**References:** Lab.Core, Lab.UI  
**Window:** 1180 × 820, title "VideoLab"

### What it does

VideoLab renders a static "visualizer" MP4: one background image + an audio spectrum-bar overlay rendered by FFmpeg. Intended for YouTube uploads: drop an audio file and a 3840×2160 image, pick a bar color, click Render.

### UI layout (`MainWindow.xaml`)

Four-row layout (Auto : * : Auto : 300):

**Row 0 — Header bar:**
- "VideoLab" title (bold, 22pt)
- Right-aligned tool-status chip (color dot + text from `Tools.StatusText`; green when `Tools.AllPresent`)

**Row 1 — Main content (two columns, 420px left / * right):**
- *Left panel* (PanelBackground): scrollable control stack
  - Audio section: path TextBox + "Browse…" button → `Render.PickAudioCommand`
  - Image section: path TextBox + "Browse…" + note "Must be exactly 3840 × 2160."
  - Output MP4: TextBox + "Save as…" button → `Render.PickOutputCommand`
  - Visualizer color: `DarkColorPicker` bound to `Render.SelectedColor`
  - Peak/accent color swatch (30% darker, auto-derived) + opacity display
  - Render / Cancel buttons with `AccentButtonStyle` / default style
  - ProgressBar (0–100, `Render.ProgressPercent`)
  - Status text + elapsed timer (`Render.ElapsedText`, Cascadia Mono font)
  - "Open output folder" button
- *Right panel*: live preview
  - `Viewbox` (Uniform stretch) containing a 1920×1080 `Border` (simulates the output frame)
  - `Image` showing the selected background image
  - "No image selected" placeholder text
  - Static 40-bar `UniformGrid` overlay at 0.5 opacity showing sample bar heights in the chosen color

**Row 2 — GridSplitter** (draggable, transparent)

**Row 3 — Log panel (dark #181818):**
- "Log" header + Copy/Clear buttons
- `ItemsControl` bound to `Terminal.Lines` (LogTextStyle, NoWrap)

### ViewModels

**`UI/ViewModels/MainViewModel.cs`**  
Root ViewModel. Constructs and exposes:
- `Render` — `VideoRenderViewModel`
- `Terminal` — `TerminalViewModel`
- `Tools` — `ToolStatusViewModel`

**`UI/ViewModels/VideoRenderViewModel.cs`**  
Single-render surface. Properties:
- `AudioPath`, `ImagePath`, `OutputPath`
- `SelectedColor` (`RgbColor`) — bound to DarkColorPicker
- `PeakColor` (`RgbColor`) — auto-derived (30% darker)
- `PeakHex`, `OpacityDisplay` — display strings
- `ProgressPercent`, `StatusText`, `ElapsedText`
- `IsPreviewPlaying` — not used yet (placeholder)
- `PreviewBars` — `IReadOnlyList<double>` of static heights for the preview overlay

Commands:
- `PickAudioCommand` / `PickImageCommand` / `PickOutputCommand` — file dialogs
- `RenderCommand` (async, cancellable) — builds a `VideoRenderJob` record, calls `VideoRenderService.RenderAsync()`, streams progress to `ProgressPercent`/`StatusText`, logs FFmpeg output to `Terminal`
- `CancelCommand` — cancels the active `CancellationTokenSource`
- `OpenOutputFolderCommand` — opens Windows Explorer to the output folder

---

## Project 5 — SongLab (GUI application)

**Target:** `net10.0-windows`, WinExe  
**NuGet:** `CommunityToolkit.Mvvm 8.4.2`  
**References:** Lab.Core, Lab.UI  
**Window:** 1100 × 820, title "SongLab"

### What it does

SongLab is an audio mastering pipeline GUI. Pick a source audio file, apply EQ (high-pass, low-pass, bass boost, treble boost), optionally time-stretch with Rubberband R3, optionally normalize loudness to a LUFS target with rsgain, choose an output format, export.

### UI layout (`MainWindow.xaml`)

Four-row layout (Auto : * : Auto : 240):

**Row 0 — Header bar:**
- "SongLab" title
- Right: tool-status chip (orange dot → green when `Tools.AllPresent`) + "Setup tools…" button

**Row 1 — Main content (two columns with a 32px spacer):**
- *Left column*: I/O and Tempo controls
  - Source: play/pause toggle button (▶/⏸), read-only path TextBox, "Browse…" → `Mastering.PickSourceCommand`
  - Output: editable TextBox, "Save as…" → `Mastering.PickOutputCommand`
  - Delivery format ComboBox: FLAC / WAV / AIFF
  - Tempo/Stretch slider: 50%–150%, snapped to 1%, displays `{value:0}%`
  - "Run mastering" button (`AccentButtonStyle`) → `Mastering.RunCommand`
  - Status text
- *Right column*: Filters and Loudness
  - Filters header + preset ComboBox (bound to `Mastering.Presets`, display path `Name`)
  - High-pass slider: 0–300 Hz
  - Low-pass slider: 0–20000 Hz
  - Bass slider: -12 to +12 dB
  - Treble slider: -12 to +12 dB
  - "Loudness (rsgain)" section:
    - "Normalize loudness" CheckBox → `Mastering.ApplyLoudness`
    - Target LUFS slider: -24 to -9, bound to `Mastering.LoudnessTargetLufs`
    - Hint text: "YouTube turns down anything hotter than ~-14 LUFS."

**Row 2 — GridSplitter**

**Row 3 — Log panel** (same pattern as VideoLab: dark border, Copy/Clear, `Terminal.Lines`)

### ViewModels

**`UI/ViewModels/MainViewModel.cs`**  
Root. Exposes `Mastering`, `Terminal`, `Tools`. Handles `OpenSetupCommand` → shows `SetupWindow`.

**`UI/ViewModels/MasteringViewModel.cs`**  
Mastering surface. Properties:
- `SourcePath`, `OutputPath`
- `DeliveryFormats` (list), `SelectedFormat`
- `TempoPercent` (double, 50–150)
- `Presets` (`ObservableCollection<EqPreset>`), `SelectedPreset`
- `HighPassHz`, `LowPassHz`, `BassDb`, `TrebleDb`
- `ApplyLoudness` (bool), `LoudnessTargetLufs`
- `IsPreviewPlaying`, `StatusText`

Commands:
- `PickSourceCommand` / `PickOutputCommand`
- `TogglePreviewCommand` — NAudio preview playback of the source file
- `RunCommand` (async, cancellable) — builds parameters, calls `MasteringPipeline.RunAsync()`, streams log output to `Terminal`

**`UI/ViewModels/SetupViewModel.cs`**  
Tool-download dialog for external tools (FFmpeg, SoX, Rubberband, rsgain).

**`Models/EqPreset.cs`**  
Record: `Name` (string), `HighPassHz` (double), `LowPassHz` (double), `BassDb` (double), `TrebleDb` (double). A few built-in presets are defined (e.g., Flat, Vocal, Bass Boost).

### Dialog Windows

**`UI/Views/SetupWindow.xaml`** — Tool installation status and download buttons.

---

## Project 6 — ImageLab (GUI application)

**Target:** `net10.0-windows`, WinExe  
**NuGet:** `CommunityToolkit.Mvvm 8.4.2`  
**References:** Lab.Core, Lab.UI  
**Window:** 1240 × 860, title "ImageLab"

### What it does

ImageLab is a non-destructive image editor. Open a source image, adjust crop/canvas/resize and color parameters, see a live debounced preview, then export the final result via ImageMagick.

### UI layout (`MainWindow.xaml`)

Four-row layout (Auto : * : Auto : 180):

**Row 0 — Header bar:**
- "ImageLab" title
- Right: tool-status chip (green when `Tools.AllPresent`)

**Row 1 — Main content (380px left / * right):**
- *Left panel* (scrollable): all edit controls
  - Source: read-only TextBox + "Open…" → `Edit.PickSourceCommand`; source dimensions text below
  - Output: editable TextBox + "Save as…" → `Edit.PickOutputCommand`
  - Canvas section: Fit mode ComboBox + Target Width/Height TextBoxes
  - Crop section: "Crop" CheckBox; when enabled: X, Y, W, H TextBoxes in a UniformGrid
  - Adjustments section: slider rows for Brightness (−100..100), Contrast (−100..100), Gamma (0.2..3.0), Saturation (0..200), Chroma (0..200, hue rotation), Red (−100..100), Green (−100..100), Blue (−100..100)
  - "Export image" button (`AccentButtonStyle`) → `Edit.ExportCommand`
  - Status text
- *Right panel*: "Live preview" — `Image` control bound to `Edit.PreviewSource`, Uniform stretch, dark background. Shows placeholder text when no image is loaded.

**Row 2 — GridSplitter**

**Row 3 — Log panel** (same pattern: dark border, Copy/Clear, `Terminal.Lines`)

### ViewModels

**`UI/ViewModels/MainViewModel.cs`**  
Root. Exposes `Edit` and `Terminal` and `Tools`.

**`UI/ViewModels/ImageEditViewModel.cs`**  
Single-image editor. Properties map 1:1 to the `ImageAdjustments` record fields plus `SourcePath`, `OutputPath`, `SelectedResizeMode`, `SourceDimensionsText`, `StatusText`, `PreviewSource` (WPF `ImageSource`).

A `DispatcherTimer` debounces preview regeneration: any property change sets a dirty flag and resets the timer; on timer tick, the current `ImageAdjustments` record is assembled and `ImageOpsService` is called on a background thread, then the result is decoded into `PreviewSource`.

Commands:
- `PickSourceCommand` — opens file dialog, calls `ImageProbeService` to populate `SourceDimensionsText`
- `PickOutputCommand` — save dialog
- `ExportCommand` (async) — calls `ImageOpsService.ExportAsync()` with the current `ImageAdjustments`

---

## Project 7 — MediaLab.Hub (GUI application — launcher)

**Target:** `net10.0-windows`, WinExe  
**NuGet:** `CommunityToolkit.Mvvm 8.4.2`  
**References:** Lab.Core, Lab.UI  
**Window:** 1000 × 700, centered on screen, title "MediaLab Hub"

### What it does

MediaLab.Hub is the entry-point launcher for the suite. It shows a 2×2 grid of application tiles; clicking a tile launches that application's executable from the sibling `Build\` folder.

### UI layout (`MainWindow.xaml`)

Four-row layout:
- Row 0: Logo image (48×48) + "MediaLab Suite" title (32pt bold)
- Row 1: "Select an application to launch:" subtitle (16pt, subtle color)
- Row 2: 2×2 `UniformGrid` of app tiles via `ItemsControl` bound to `Apps`. Each tile is a custom-template `Button` (rounded 12px corner, hover highlight) containing: 96×96 app icon + right-side StackPanel with app title (24pt bold) and description (14pt, wrapping, subtle color). Clicking fires `LaunchAppCommand` with the tile's `AppTile` record as parameter.
- Row 3: `StatusMessage` TextBlock (orange, centered) — shown when an exe is not found

### ViewModel

**`ViewModels/MainViewModel.cs`**  
- `Apps` — `ObservableCollection<AppTile>`. Four tiles: LyricsLab, VideoLab, SongLab, ImageLab — each with Title, Description, IconPath (pack URI), and ExeName.
- `StatusMessage` — error text
- `LaunchAppCommand(AppTile)` — resolves the exe path relative to the Hub's own location (`../AppName/AppName.exe`), calls `Process.Start()`. Sets `StatusMessage` if not found.

---

## Project 8 — MediaLab.Tests (test project)

**Target:** `net10.0` (not windows-specific)  
**NuGet:** `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.4`, `Microsoft.NET.Test.Sdk 17.14.1`, `coverlet.collector 6.0.4`  
**References:** Lab.Core only

### Test files

**Lyrics/**
- `LyricsAlignerTests.cs` — tests Needleman-Wunsch alignment with various lyric/transcript combinations
- `LyricsXmlParserTests.cs` — round-trip XML parse tests
- `TimingIssueDetectorTests.cs` — overlap, gap, long-cue detection
- `SubtitleWriterTests.cs` — SRT/LRC output format verification
- `WhisperCppJsonParserTests.cs` — JSON parsing from Whisper CLI
- `OracleTranscriberServiceTests.cs` — oracle parallel-engine behavior (with mocked runners)
- `WhisperTranscriberServiceIntegrationTests.cs` — requires Whisper installed (skips otherwise)

**Media/**
- `VideoRenderServiceIntegrationTests.cs` — requires FFmpeg
- `VideoRenderJobTests.cs` — job record validation
- `SpectrumBarFilterBuilderTests.cs` — filter-graph string generation
- `FfmpegProgressParserTests.cs` — progress parsing
- `FfprobeServiceIntegrationTests.cs` — requires FFprobe
- `FfprobeParseTests.cs` — duration string parsing
- `OutputPathBuilderTests.cs` — path derivation
- `InstrumentalDetectorServiceTests.cs` — gap detection logic
- `ColorMathTests.cs` — color parsing/conversion
- `ImageProbeParseTests.cs` — dimension parsing

**Imaging/**
- `ImageMagickArgsBuilderTests.cs` — arg string generation
- `ImageOpsServiceIntegrationTests.cs` — requires ImageMagick

**Processes/**
- `ProcessRunnerTests.cs` — subprocess execution

**Tools/**
- `ToolLocatorTests.cs` — tool discovery logic

**Logging/**
- `BoundedLogBufferTests.cs` — circular buffer behavior

**TestSupport/**
- `TempDir.cs` — `IDisposable` temporary directory helper
- `RealTools.cs` — finds real installed tools for integration tests
- `ListProgress.cs` — `IProgress<T>` implementation collecting results for assertions

---

## Build Configuration

**`Directory.Build.props`** (applies to all projects):
- `<LangVersion>14</LangVersion>`
- `<Nullable>enable</Nullable>`
- `<ImplicitUsings>enable</ImplicitUsings>`
- `<PlatformTarget>x64</PlatformTarget>`
- `<OutputPath>$(SolutionDir)Build\$(ProjectName)\</OutputPath>` (for WinExe targets)
- `<EnableNETAnalyzers>true</EnableNETAnalyzers>` at `AnalysisLevel=latest`

**`MediaLab.slnx`** — new `.slnx` solution XML format (Visual Studio 2022 17.9+).

---

## Architecture Patterns

1. **MVVM via CommunityToolkit.Mvvm.** All ViewModels inherit `ObservableObject`. Properties use `[ObservableProperty]` source generators. Commands use `[RelayCommand]` (which generates async-aware, cancellable command implementations automatically). No code-behind logic.

2. **No DI container.** Services are constructed directly in ViewModels or passed as constructor arguments. `ToolLocator` is the only "locator" — everything else is explicit construction.

3. **Immutable records for data.** `VideoRenderJob`, `AlignedLine`, `TimedWord`, `ImageAdjustments`, `LyricLineData`, `ProcessResult` etc. are all C# records. Mutations produce new instances.

4. **`IProcessRunner` abstraction.** All CLI tool invocations go through `IProcessRunner`, enabling mocking in unit tests.

5. **Consistent shell structure across apps.** Every app has: header with title + tool-status chip, main content area with left controls + right preview, draggable GridSplitter, bottom terminal/log panel. `TerminalViewModel` and `ToolStatusViewModel` are reused from Lab.UI.

6. **Progress reporting.** Long-running operations accept `IProgress<T>` callbacks and `CancellationToken`. The `FfmpegProgressParser` converts stderr line-by-line to percent values.

7. **Portable tool distribution.** Tools can live in `tools/` next to the executables; `ToolLocator` finds them. `FfmpegInstallerService` and `WhisperInstallerService` can download and install tools to that directory from within the app.

8. **Build output convention.** All apps compile to `Build\<AppName>\<AppName>.exe`. MediaLab.Hub resolves sibling exe paths relative to its own assembly location.

---

## External Tool Dependencies

| Tool | Used by | Purpose |
|------|---------|---------|
| `ffmpeg` | VideoLab, SongLab | Encode/mux video and audio |
| `ffprobe` | VideoLab, LyricsLab | Query media duration and image dimensions |
| `magick` (ImageMagick) | ImageLab | Image processing and export |
| `sox` | SongLab | DSP: EQ, compression |
| `rubberband` | SongLab | Time-stretching (R3 engine) |
| `rsgain` | SongLab | Loudness normalization (ReplayGain/LUFS) |
| `whisper` (OpenAI CLI) | LyricsLab | Speech-to-text |
| `whisperx` (Python venv) | LyricsLab | Forced-alignment transcription |
| `vosk` (NuGet + offline models) | LyricsLab | Offline fallback transcription |
| `pyannote` (inside WhisperX venv) | LyricsLab | VAD for instrumental detection |

---

## Known Issues / Technical Debt

1. **WhisperX over-stretches words across instrumentals.** When the whole song is a single segment, forced alignment spreads words across long instrumental gaps (14+ seconds). Workaround: run transcription first, then alignment as a separate step.
2. **Large Whisper models hang on CPU.** `large-v3` is unusable without a GPU. The UI defaults to `small`. `tiny` produces hallucinated output. No GPU detection in the UI.
3. **LyricsLab has dual ViewModel directories.** `ViewModels/` (legacy `MainViewModel.cs`, `LyricLineViewModel.cs`) and `UI/ViewModels/` (newer `LyricsEditorViewModel.cs`, `SetupViewModel.cs`, `SpeechModelItem.cs`). These are not consolidated.
4. **No subtitle export beyond SRT/LRC in LyricsLab.** `SubtitleWriter` in Lab.Core supports SRT and LRC. Other formats (VTT, ASS, etc.) are not wired to the UI.
5. **`libtorchcodec_core7.dll` warning.** Pyannote logs a warning when torchcodec is absent; it gracefully falls back to torchaudio/soundfile. Not a blocking issue.
6. **No UI tests.** WPF views are untested. Only Lab.Core has test coverage.

---

## Planned Work (LyricsLab V3)

A V3 redesign is planned as a replacement for the current LyricsLab. Key design decisions already made:

- **Four-stage workflow** (Setup → Structure Editor → Transcribe & Align → Export), replacing the current single-window layout
- **VAD-based instrumental detection** using `vad_detect.py` + pyannote (already implemented in `InstrumentalDetectorService`); replaces any silence-detect approach
- **OracleTranscriberService** promoted from LyricsLab to Lab.Core (already done)
- **Export formats**: SRT, LRC, VTT, ASS, SBV, SUB, JSON via `SubtitleWriter`
- **Implementation phases**: Scaffold new project → Four-panel UI → Wire InstrumentalDetector → Wire OracleTranscriberService → Replace LyricsLab in solution

The `LyricsXmlParser`, `LyricsXmlWriter`, `SongLyrics` model (with `IsInstrumental` / `DurationHint` already on sections), `LyricsAligner` (already skips instrumentals), and `SubtitleWriter` are all carry-forward reuse targets.
