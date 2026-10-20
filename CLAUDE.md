# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test

```powershell
# Build entire solution (Debug)
dotnet build MediaLab.slnx

# Build Release (outputs to Build\<AppName>\)
.\build-release.ps1

# Run all tests
dotnet test MediaLab.Tests

# Run a single test class or method
dotnet test MediaLab.Tests --filter "ClassName"
dotnet test MediaLab.Tests --filter "FullyQualifiedName~MethodName"
```

Build output for all WPF apps goes to `Build\<AppName>\<AppName>.exe` (flat, no framework subfolder). Libraries and the test project keep their default `bin/` paths. Debug and Release builds share the same `Build\` path — Release overwrites Debug.

## Architecture

**Two shared libraries, five apps, one test project.** Dependency flow: `Lab.Core` ← `Lab.UI` ← WPF apps. `MediaLab.Tests` references only `Lab.Core`.

### Lab.Core
Pure business logic; no WPF references. Key areas:
- `Lyrics/` — `SongLyrics` data model (sections → lines → words), XML parser/writer, `LyricsAligner` (Needleman-Wunsch DP over `TimedWord[]`), `SubtitleWriter` (SRT/LRC), `OracleTranscriberService` (runs WhisperX + Whisper + Vosk in parallel via `Task.WhenAny`)
- `Media/` — `MasteringPipeline` (FFmpeg → SoX → Rubberband → rsgain chain), `VideoRenderService` + `SpectrumBarFilterBuilder` (FFmpeg lavfi filter graph), `InstrumentalDetectorService` (invokes `vad_detect.py`, returns non-vocal gaps)
- `Imaging/` — `ImageAdjustments` record → `ImageMagickArgsBuilder` → `ImageOpsService`
- `Processes/` — `IProcessRunner` / `ProcessRunner` (all CLI calls go through this interface; test doubles use mocks)
- `Tools/` — `ToolLocator` (multi-stage: configured path → PATH → `tools/` sibling dir → legacy `D:\cli\tools`), `DependencyVerifier`
- `Scripts/vad_detect.py` — Python; runs inside the WhisperX venv; invokes pyannote VAD, outputs JSON `{non_vocal:[…], vocal:[…]}`

### Lab.UI
Shared WPF components reused by all apps:
- `TerminalViewModel` — thread-safe, batched via `DispatcherTimer`; use `Append(string)` from any thread
- `ToolStatusViewModel` — wraps `DependencyVerifier`; appears in every app header
- `DarkTheme.xaml` — single resource dictionary; all brush names (`WindowBackgroundBrush`, `AccentBrush`, etc.) and styles (`AccentButtonStyle`, `LogTextStyle`) live here
- `DarkColorPicker` control, value converters, `FilePickerService`

### App structure (all four apps follow the same shell)
Row 0: header (title + `ToolStatusViewModel` chip) | Row 1: left controls + right preview (`*`) | Row 2: `GridSplitter` | Row 3: terminal log panel (240–300px, `TerminalViewModel.Lines`)

## Key Patterns

**MVVM via CommunityToolkit.Mvvm.** All ViewModels extend `ObservableObject`. Use `[ObservableProperty]` and `[RelayCommand]` source generators — no manual `INotifyPropertyChanged` boilerplate. `[RelayCommand]` on an `async Task` method auto-generates a cancellable `IAsyncRelayCommand`.

**No DI container.** Services are constructed directly in ViewModels. Pass `ToolLocator` explicitly where needed.

**Immutable records for data.** `VideoRenderJob`, `AlignedLine`, `TimedWord`, `ImageAdjustments`, `ProcessResult`, `LyricLineData` are all `record` types. Produce new instances rather than mutating.

**`IProcessRunner` for all CLI calls.** Never spawn `Process` directly outside of `ProcessRunner`. This keeps services unit-testable with a mock runner.

**`InternalsVisibleTo`.** `Lab.Core` exposes internals (e.g., `LyricsAligner.Normalize`, `LyricsAligner.Similarity`) to `MediaLab.Tests` via `<InternalsVisibleTo Include="MediaLab.Tests" />`.

**Integration tests are skipped when tools are absent.** `RealTools.cs` in `TestSupport/` locates installed binaries; integration tests skip themselves via `Skip` when the tool is not found.

## Known Issues

- **LyricsLab has two ViewModel directories.** `ViewModels/` (legacy `MainViewModel`, `LyricLineViewModel`) and `UI/ViewModels/` (newer `LyricsEditorViewModel`, `SetupViewModel`). They are not consolidated.
- **WhisperX forced-alignment stretches words across instrumental gaps** when the whole song is one segment. The fix is to run transcription first, then alignment separately (already implemented in `whisperx_driver.py`).
- **Whisper `large-v3` hangs on CPU.** No GPU detection in the UI; default is `small`.
- **No UI tests.** Only `Lab.Core` has test coverage.

## Planned: LyricsLab V3

A full rewrite of LyricsLab as a four-stage wizard (Setup → Structure Editor → Transcribe & Align → Export) is planned. Reuse targets already in `Lab.Core`: `SongLyrics` model, `LyricsXmlParser`/`LyricsXmlWriter`, `LyricsAligner`, `SubtitleWriter`, `InstrumentalDetectorService`, `OracleTranscriberService`. See `memory/project_lyricslab_v3.md` for the design spec.

## External Tool Dependencies

| Tool | App | Notes |
|------|-----|-------|
| `ffmpeg` / `ffprobe` | VideoLab, SongLab, LyricsLab | `FfmpegInstallerService` can download from gyan.dev |
| `magick` (ImageMagick) | ImageLab | |
| `sox` | SongLab | DSP chain |
| `rubberband` | SongLab | R3 engine time-stretch |
| `rsgain` | SongLab | ReplayGain/LUFS normalization |
| `whisper` (OpenAI CLI) | LyricsLab | |
| `whisperx` (Python venv) | LyricsLab | `WhisperInstallerService` manages install; `vad_detect.py` needs pyannote inside the venv |
| `vosk` (NuGet + offline models) | LyricsLab | Offline fallback; `WhisperInstallerService` handles model download |

Tools are located by `ToolLocator` in priority order: app-configured path → `PATH` env → `tools/` sibling to the exe → `D:\cli\tools`. Download via the in-app Setup window.
