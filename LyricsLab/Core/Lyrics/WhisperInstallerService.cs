using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Lab.Core.Processes;
using Lab.Core.Tools;

namespace LyricsLab.Core.Lyrics;

/// <summary>
/// Downloads and installs speech engine binaries and model files.
/// Whisper: latest GitHub release for ggerganov/whisper.cpp (Windows x64 CPU build) +
///          ggml model files from the HuggingFace ggerganov/whisper.cpp repo.
/// Vosk:    model zip archives from alphacephei.com (no separate binary needed — uses NuGet).
/// All assets install into <see cref="DefaultInstallDir"/>.
/// </summary>
public sealed class WhisperInstallerService
{
    /// <summary>
    /// Where new installs and bundled assets live: the portable <c>&lt;app&gt;\tools\whisper</c> dir.
    /// Status/discovery additionally probe <see cref="InstallSearchDirs"/> so an existing legacy
    /// install keeps working.
    /// </summary>
    public static readonly string DefaultInstallDir = Path.Combine(ToolDefaults.PortableRoot, "whisper");

    /// <summary>Legacy developer install dir (the author's centralized folder); probed after the portable dir.</summary>
    private static readonly string LegacyInstallDir = Path.Combine(ToolDefaults.LegacyDevRoot, "whisper");

    /// <summary>Install dirs probed for already-present assets, in priority order: portable beside-exe, then legacy.</summary>
    public static IReadOnlyList<string> InstallSearchDirs { get; } = [DefaultInstallDir, LegacyInstallDir];

    /// <summary>The dirs to search for an existing asset: the caller's explicit dir, else portable → legacy.</summary>
    private static IReadOnlyList<string> SearchDirs(string? overrideDir) =>
        overrideDir is not null ? [overrideDir] : InstallSearchDirs;

    private const string GitHubApiLatest =
        "https://api.github.com/repos/ggerganov/whisper.cpp/releases/latest";
    private const string HuggingFaceBase =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
    private const string AlphaCepheiBase =
        "https://alphacephei.com/vosk/models/";

    public static readonly IReadOnlyList<SpeechModelInfo> KnownModels =
    [
        // ── Whisper models ───────────────────────────────────────────────────
        new(SpeechEngine.Whisper, "tiny · English",   "ggml-tiny.en.bin",   75,   "Fastest, English only"),
        new(SpeechEngine.Whisper, "base · English",   "ggml-base.en.bin",   141,  "Fast, English only", IsRecommended: true),
        new(SpeechEngine.Whisper, "small · English",  "ggml-small.en.bin",  461,  "Good quality, English only", IsRecommended: true),
        new(SpeechEngine.Whisper, "medium · English", "ggml-medium.en.bin", 1457, "High quality, English only"),
        new(SpeechEngine.Whisper, "large-v3",         "ggml-large-v3.bin",  2952, "Best quality, multilingual (WARNING: 16GB+ RAM required, extremely slow)", IsWarning: true),

        // ── Vosk models ──────────────────────────────────────────────────────
        new(SpeechEngine.Vosk, "small · English", "vosk-model-small-en-us-0.15.zip", 40,
            "Fast, Kaldi-based — good fallback for distorted audio", IsRecommended: true),
        new(SpeechEngine.Vosk, "large · English", "vosk-model-en-us-0.22.zip", 1800,
            "High quality Kaldi model"),
    ];

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        DefaultRequestHeaders = { { "User-Agent", "LyricsLab/1.0" } },
        Timeout = TimeSpan.FromMinutes(90),
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Status queries
    // ─────────────────────────────────────────────────────────────────────────

    public static bool IsWhisperBinaryInstalled(string? dir = null) =>
        SearchDirs(dir).Any(d => File.Exists(Path.Combine(d, "whisper-cli.exe")));

    /// <summary>True when the whisperX venv python and driver script are both present in any search dir.</summary>
    public static bool IsWhisperXInstalled(string? dir = null) =>
        SearchDirs(dir).Any(d =>
            File.Exists(WhisperXTranscriberService.VenvPython(d))
            && File.Exists(WhisperXTranscriberService.DriverPath(d)));

    /// <summary>
    /// The install dir whisperX should run from: the first search dir that already holds a venv +
    /// driver, else the portable <see cref="DefaultInstallDir"/> (where a fresh install would land).
    /// </summary>
    public static string ResolveWhisperXInstallDir() =>
        InstallSearchDirs.FirstOrDefault(d =>
            File.Exists(WhisperXTranscriberService.VenvPython(d))
            && File.Exists(WhisperXTranscriberService.DriverPath(d)))
        ?? DefaultInstallDir;

    /// <summary>Returns which whisper ggml-*.bin files are present across the search dirs.</summary>
    public static IReadOnlySet<string> InstalledWhisperModels(string? dir = null)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in SearchDirs(dir))
        {
            if (!Directory.Exists(d)) continue;
            foreach (var name in Directory.GetFiles(d, "ggml-*.bin").Select(Path.GetFileName).OfType<string>())
                set.Add(name);
        }
        return set;
    }

    /// <summary>Returns which Vosk model directories are present (by base name, no .zip) across the search dirs.</summary>
    public static IReadOnlySet<string> InstalledVoskModels(string? dir = null)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in SearchDirs(dir))
        {
            if (!Directory.Exists(d)) continue;
            foreach (var name in Directory.GetDirectories(d, "vosk-model-*").Select(Path.GetFileName).OfType<string>())
                set.Add(name);
        }
        return set;
    }

    public bool IsModelInstalled(SpeechModelInfo model, string? dir = null) =>
        model.Engine == SpeechEngine.Whisper
            ? InstalledWhisperModels(dir).Contains(model.FileName)
            : InstalledVoskModels(dir).Contains(VoskDirName(model.FileName));

    // ─────────────────────────────────────────────────────────────────────────
    // Installation — binary
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the latest whisper-cli Windows x64 CPU binary from GitHub releases and
    /// extracts whisper-cli.exe (and required .dll files) to <paramref name="installDir"/>.
    /// </summary>
    public async Task InstallWhisperBinaryAsync(
        string? installDir = null,
        Action<string>? onLog = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var dir = installDir ?? DefaultInstallDir;
        Directory.CreateDirectory(dir);

        onLog?.Invoke("[setup] Querying GitHub for latest whisper.cpp release…");
        var (url, assetName) = await FindWindowsAssetAsync(ct);
        onLog?.Invoke($"[setup] Found: {assetName}");

        var tempZip = Path.Combine(Path.GetTempPath(), $"whisper_{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadFileAsync(url, tempZip, "[setup]", onLog, progress, ct);
            onLog?.Invoke("[setup] Extracting…");
            ExtractWhisperBinary(tempZip, dir, onLog);
            onLog?.Invoke($"[setup] whisper-cli installed → {dir}");
        }
        finally
        {
            try { File.Delete(tempZip); } catch { /* best-effort */ }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Installation — whisperX (Python venv)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a dedicated Python venv and installs whisperX into it: PyTorch (CUDA 12.1 build, so
    /// the GPU is used when present), then whisperX (which pulls faster-whisper + the alignment
    /// stack). Finally writes the driver script. This downloads several GB and can take many
    /// minutes; all child output streams to <paramref name="onLog"/>. The faster-whisper and
    /// wav2vec2 models download on first use, not here.
    /// </summary>
    public async Task InstallWhisperXAsync(
        string? installDir = null,
        Action<string>? onLog = null,
        CancellationToken ct = default)
    {
        var dir = installDir ?? DefaultInstallDir;
        Directory.CreateDirectory(dir);

        var basePython = ResolveBasePython()
            ?? throw new InvalidOperationException(
                "Python 3 was not found. Install Python 3.10–3.12 and ensure 'python' is on PATH.");

        var runner = new ProcessRunner();

        var versionCheck = await runner.RunAsync(basePython, ["-c", "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')"], captureOutput: true, cancellationToken: ct);
        var pyVersion = versionCheck.StandardOutput?.Trim() ?? "";
        if (versionCheck.ExitCode != 0 || (!pyVersion.StartsWith("3.10") && !pyVersion.StartsWith("3.11") && !pyVersion.StartsWith("3.12")))
        {
            throw new InvalidOperationException($"Unsupported Python version '{pyVersion}'. Please install Python 3.10, 3.11, or 3.12.");
        }

        var venvDir    = WhisperXTranscriberService.VenvDir(dir);
        var venvPython = WhisperXTranscriberService.VenvPython(dir);

        onLog?.Invoke($"[setup] Using base Python: {basePython} ({pyVersion})");
        onLog?.Invoke($"[setup] Creating venv at {venvDir}…");
        await RunStep(runner, basePython, ["-m", "venv", venvDir], onLog, ct);

        onLog?.Invoke("[setup] Upgrading pip…");
        await RunStep(runner, venvPython, ["-m", "pip", "install", "--upgrade", "pip"], onLog, ct);

        var nvidiaSmi = await runner.RunAsync("nvidia-smi", [], captureOutput: true, cancellationToken: ct);
        var hasGpu = nvidiaSmi.ExitCode == 0;

        onLog?.Invoke(hasGpu
            ? "[setup] GPU detected. Installing PyTorch (CUDA 12.1)… (~2.5 GB)"
            : "[setup] No GPU detected. Installing PyTorch (CPU)…");

        var torchArgs = hasGpu
            ? new[] { "-m", "pip", "install", "torch==2.3.1", "torchaudio==2.3.1", "--index-url", "https://download.pytorch.org/whl/cu121" }
            : new[] { "-m", "pip", "install", "torch==2.3.1", "torchaudio==2.3.1", "--index-url", "https://download.pytorch.org/whl/cpu" };
        await RunStep(runner, venvPython, torchArgs, onLog, ct);

        onLog?.Invoke("[setup] Installing whisperX and dependencies…");
        await RunStep(runner, venvPython, ["-m", "pip", "install", "faster-whisper==1.0.3", "ctranslate2==4.4.0", "whisperx==3.1.1"], onLog, ct);

        onLog?.Invoke("[setup] Writing driver script…");
        await File.WriteAllTextAsync(
            WhisperXTranscriberService.DriverPath(dir),
            WhisperXTranscriberService.DriverScript, ct);

        onLog?.Invoke($"[setup] whisperX installed → {venvDir}");
    }

    private static async Task RunStep(
        ProcessRunner runner, string exe, IEnumerable<string> args,
        Action<string>? onLog, CancellationToken ct)
    {
        var result = await runner.RunAsync(
            exe, args, onOutputLine: onLog, onErrorLine: onLog,
            captureOutput: false, cancellationToken: ct);

        if (result.Canceled)
            throw new OperationCanceledException(ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Step failed (exit {result.ExitCode}): {Path.GetFileName(exe)} {string.Join(' ', args)}");
    }

    /// <summary>
    /// Locates a base Python interpreter on PATH for building the venv. Prefers the Windows <c>py</c>
    /// launcher, then <c>python</c>. Returns null if neither is on PATH (the caller surfaces a clear
    /// "install Python 3.10–3.12" message).
    /// </summary>
    private static string? ResolveBasePython()
    {
        foreach (var command in new[] { "py", "python" })
        {
            if (FoundOnPath(command))
                return command;
        }
        return null;
    }

    /// <summary>True when <paramref name="command"/> (without extension) resolves to an .exe on PATH.</summary>
    private static bool FoundOnPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return false;

        var exe = command + ".exe";
        foreach (var dir in path.Split(
            Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try { if (File.Exists(Path.Combine(dir, exe))) return true; }
            catch (ArgumentException) { /* malformed PATH entry — skip */ }
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Installation — models
    // ─────────────────────────────────────────────────────────────────────────

    public async Task DownloadModelAsync(
        SpeechModelInfo model,
        string? installDir = null,
        Action<string>? onLog = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (model.Engine == SpeechEngine.Whisper)
            await DownloadWhisperModelAsync(model, installDir, onLog, progress, ct);
        else
            await DownloadVoskModelAsync(model, installDir, onLog, progress, ct);
    }

    private async Task DownloadWhisperModelAsync(
        SpeechModelInfo model, string? installDir,
        Action<string>? onLog, IProgress<double>? progress, CancellationToken ct)
    {
        var dir = installDir ?? DefaultInstallDir;
        Directory.CreateDirectory(dir);
        var url  = HuggingFaceBase + model.FileName;
        var dest = Path.Combine(dir, model.FileName);
        onLog?.Invoke($"[setup] Downloading {model.FileName} (~{model.SizeMb} MB)…");
        await DownloadFileAsync(url, dest, "[setup]", onLog, progress, ct);
        onLog?.Invoke($"[setup] {model.FileName} → {dir}");
    }

    private async Task DownloadVoskModelAsync(
        SpeechModelInfo model, string? installDir,
        Action<string>? onLog, IProgress<double>? progress, CancellationToken ct)
    {
        var dir = installDir ?? DefaultInstallDir;
        Directory.CreateDirectory(dir);
        var url     = AlphaCepheiBase + model.FileName;
        var tempZip = Path.Combine(Path.GetTempPath(), $"vosk_{Guid.NewGuid():N}.zip");

        onLog?.Invoke($"[setup] Downloading {model.FileName} (~{model.SizeMb} MB)…");
        try
        {
            await DownloadFileAsync(url, tempZip, "[setup]", onLog, progress, ct);
            onLog?.Invoke("[setup] Extracting Vosk model…");
            ZipFile.ExtractToDirectory(tempZip, dir, overwriteFiles: true);
            onLog?.Invoke($"[setup] {VoskDirName(model.FileName)} → {dir}");
        }
        finally
        {
            try { File.Delete(tempZip); } catch { /* best-effort */ }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Path helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the absolute path of the best available Vosk model directory, or null if none are
    /// installed. Prefers a grammar-capable "small" model: LyricsLab runs Vosk in lyric-constrained
    /// (grammar) mode, which only the small models support — the large model can't use it and would
    /// fall back to open recognition (which flails on sung vocals). Falls back to any model present.
    /// </summary>
    public static string? FindVoskModelDir(string? installDir = null)
    {
        foreach (var d in SearchDirs(installDir))
        {
            if (!Directory.Exists(d))
                continue;

            var best = Directory.GetDirectories(d, "vosk-model-*")
                .OrderByDescending(p => Path.GetFileName(p)
                    .Contains("small", StringComparison.OrdinalIgnoreCase)) // grammar-capable first
                .ThenByDescending(p => p, StringComparer.OrdinalIgnoreCase)  // then larger/newer name
                .FirstOrDefault();

            if (best is not null)
                return best;
        }
        return null;
    }

    /// <summary>Strips the .zip suffix to get the directory name a Vosk model extracts to.</summary>
    public static string VoskDirName(string zipName) =>
        zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? zipName[..^4]
            : zipName;

    // ─────────────────────────────────────────────────────────────────────────
    // Internals
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(string Url, string Name)> FindWindowsAssetAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(GitHubApiLatest, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("assets", out var assets))
            throw new InvalidOperationException(
                "GitHub API response missing 'assets'. Rate-limited?");

        string? bestUrl = null, bestName = null;
        var bestScore = -1;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var url  = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

            var lower = name.ToLowerInvariant();
            if (!lower.Contains("win") && !lower.Contains("mingw")) continue;

            var score = 0;
            if (!lower.Contains("cuda") && !lower.Contains("cublas") && !lower.Contains("vulkan"))
                score += 10;
            if (lower.Contains("x64") || lower.Contains("win64") || lower.Contains("x86_64"))
                score += 5;
            if (lower.Contains("avx2"))
                score += 1;

            if (score > bestScore)
            {
                bestScore = score;
                bestUrl  = url;
                bestName = name;
            }
        }

        if (bestUrl is null)
            throw new InvalidOperationException(
                "No Windows x64 CPU build found in the latest whisper.cpp release. " +
                "See https://github.com/ggerganov/whisper.cpp/releases for manual download.");

        return (bestUrl, bestName!);
    }

    private static void ExtractWhisperBinary(string zipPath, string destDir, Action<string>? onLog)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        foreach (var targetName in new[] { "whisper-cli.exe", "main.exe" })
        {
            var entry = zip.Entries.FirstOrDefault(e =>
                string.Equals(Path.GetFileName(e.FullName), targetName,
                    StringComparison.OrdinalIgnoreCase));
            if (entry is null) continue;
            var dest = Path.Combine(destDir, "whisper-cli.exe");
            entry.ExtractToFile(dest, overwrite: true);
            onLog?.Invoke($"[setup] → {dest}");
            break;
        }

        foreach (var entry in zip.Entries.Where(e =>
            e.Name.Length > 0 &&
            e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            var dest = Path.Combine(destDir, entry.Name);
            entry.ExtractToFile(dest, overwrite: true);
            onLog?.Invoke($"[setup] → {dest}");
        }
    }

    internal static async Task DownloadFileAsync(
        string url, string destPath, string prefix,
        Action<string>? onLog, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var src  = await response.Content.ReadAsStreamAsync(ct);
        await using var dest = File.Create(destPath);

        var buf      = new byte[81920];
        long dlBytes = 0;
        var  nextLog = 10L * 1024 * 1024;
        int  read;

        while ((read = await src.ReadAsync(buf, ct)) > 0)
        {
            await dest.WriteAsync(buf.AsMemory(0, read), ct);
            dlBytes += read;
            if (total > 0)
                progress?.Report((double)dlBytes / total);
            else if (dlBytes >= nextLog)
            {
                onLog?.Invoke($"{prefix} {dlBytes / 1_048_576} MB…");
                nextLog += 10L * 1024 * 1024;
            }
        }
        progress?.Report(1.0);
    }
}
