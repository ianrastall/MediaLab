using System;
using System.IO;
using System.IO.Compression;
using System.IO.Enumeration;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Lab.Core.Tools;

public sealed class ZipInstallerService
{
    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        DefaultRequestHeaders = { { "User-Agent", "MediaLab/1.0" } },
        Timeout = TimeSpan.FromMinutes(90),
    };

    private readonly HttpClient _httpClient;

    public ZipInstallerService() : this(SharedHttpClient)
    {
    }

    internal ZipInstallerService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Downloads a ZIP file from the given URL and extracts selected files into the target
    /// directory. Selection supports simple wildcards such as <c>*.dll</c>. If
    /// <paramref name="targetFileNames"/> is null, it extracts the entire ZIP.
    /// </summary>
    public async Task InstallFromZipAsync(
        string url,
        string targetDir,
        string[]? targetFileNames,
        Action<string> log,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        string? expectedSha256 = null)
    {
        Directory.CreateDirectory(targetDir);
        var tempZip = Path.Combine(targetDir, $"temp_{Guid.NewGuid()}.zip");

        try
        {
            log($"Downloading from {url}...");
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalRead += bytesRead;
                if (totalBytes != -1)
                {
                    progress?.Report((double)totalRead / totalBytes);
                }
            }

            await fileStream.FlushAsync(cancellationToken);
            fileStream.Close();

            EnsureZipSignature(tempZip);

            if (!string.IsNullOrEmpty(expectedSha256))
            {
                log("Verifying SHA-256 checksum...");
                using var fs = File.OpenRead(tempZip);
                using var sha256 = SHA256.Create();
                var hashBytes = await sha256.ComputeHashAsync(fs, cancellationToken);
                var hashHex = Convert.ToHexString(hashBytes);
                if (!hashHex.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Checksum mismatch! Expected {expectedSha256}, got {hashHex}. The download may be corrupted or compromised.");
                }
                log("Checksum OK.");
            }

            log("Download complete. Extracting...");

            using var archive = ZipFile.OpenRead(tempZip);
            if (targetFileNames != null && targetFileNames.Length > 0)
            {
                // Resolve every requested name/pattern before extracting anything, so a partial
                // install can never be reported as successful.
                var selectedEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                var missingPatterns = new List<string>();

                foreach (var pattern in targetFileNames)
                {
                    var matches = archive.Entries
                        .Where(e => !string.IsNullOrEmpty(e.Name)
                            && FileSystemName.MatchesSimpleExpression(pattern, e.Name, ignoreCase: true))
                        .ToList();

                    if (matches.Count == 0)
                    {
                        missingPatterns.Add(pattern);
                    }
                    else
                    {
                        foreach (var entry in matches)
                        {
                            // Selected files are intentionally flattened into the tool folder.
                            selectedEntries.TryAdd(entry.Name, entry);
                        }
                    }
                }

                if (missingPatterns.Count > 0)
                {
                    throw new InvalidDataException(
                        $"ZIP archive is missing required file(s): {string.Join(", ", missingPatterns)}.");
                }

                foreach (var (fileName, entry) in selectedEntries)
                {
                    var destPath = Path.Combine(targetDir, fileName);
                    entry.ExtractToFile(destPath, overwrite: true);
                    log($"Extracted {fileName} to {targetDir}");
                }
            }
            else
            {
                // Extract all
                ZipFile.ExtractToDirectory(tempZip, targetDir, overwriteFiles: true);
                log($"Extracted all files to {targetDir}");
            }

            progress?.Report(1.0);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                try { File.Delete(tempZip); } catch { /* ignore */ }
            }
        }
    }

    private static void EnsureZipSignature(string path)
    {
        Span<byte> signature = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        if (stream.Read(signature) != signature.Length
            || signature[0] != 0x50
            || signature[1] != 0x4B
            || (signature[2], signature[3]) is not ((0x03, 0x04) or (0x05, 0x06) or (0x07, 0x08)))
        {
            throw new InvalidDataException(
                "The download is not a ZIP archive. The server may have returned an HTML download page instead.");
        }
    }
}
