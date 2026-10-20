using System.IO.Compression;
using System.Net.Http;
using Lab.Core.Tools;

namespace Lab.Core.Tools;

public sealed class FfmpegInstallerService
{
    private const string GyanFfmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    public static readonly string DefaultInstallDir = Path.Combine(ToolDefaults.PortableRoot, "ffmpeg");
    
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        DefaultRequestHeaders = { { "User-Agent", "MediaLab/1.0" } },
        Timeout = TimeSpan.FromMinutes(90),
    };

    public static bool IsFfmpegInstalled(string? dir = null)
    {
        var d = dir ?? DefaultInstallDir;
        return File.Exists(Path.Combine(d, "ffmpeg.exe")) && File.Exists(Path.Combine(d, "ffprobe.exe"));
    }

    public async Task InstallFfmpegAsync(
        string? installDir = null,
        Action<string>? onLog = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default,
        string? expectedSha256 = null)
    {
        var dir = installDir ?? DefaultInstallDir;
        Directory.CreateDirectory(dir);

        var tempZip = Path.Combine(Path.GetTempPath(), $"ffmpeg_{Guid.NewGuid():N}.zip");
        try
        {
            onLog?.Invoke("[setup] Downloading FFmpeg (essentials build) from gyan.dev…");
            await DownloadFileAsync(GyanFfmpegUrl, tempZip, "[setup]", onLog, progress, ct);
            
            if (!string.IsNullOrEmpty(expectedSha256))
            {
                onLog?.Invoke("[setup] Verifying SHA-256 checksum...");
                using var fs = File.OpenRead(tempZip);
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hashBytes = await sha256.ComputeHashAsync(fs, ct);
                var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                if (hashHex != expectedSha256.ToLowerInvariant())
                {
                    throw new InvalidOperationException($"Checksum mismatch! Expected {expectedSha256}, got {hashHex}. The download may be corrupted or compromised.");
                }
                onLog?.Invoke("[setup] Checksum OK.");
            }

            onLog?.Invoke("[setup] Extracting FFmpeg…");
            ExtractFfmpegBinary(tempZip, dir, onLog);
            onLog?.Invoke($"[setup] FFmpeg installed → {dir}");
        }
        finally
        {
            try { File.Delete(tempZip); } catch { /* best-effort */ }
        }
    }

    private static void ExtractFfmpegBinary(string zipPath, string destDir, Action<string>? onLog)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        foreach (var targetName in new[] { "ffmpeg.exe", "ffprobe.exe", "ffplay.exe" })
        {
            var entry = zip.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, targetName, StringComparison.OrdinalIgnoreCase));
            
            if (entry is null) continue;
            
            var dest = Path.Combine(destDir, targetName);
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
