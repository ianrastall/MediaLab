using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Lab.Core.Tools;
using MediaLab.Tests.TestSupport;

namespace MediaLab.Tests.Tools;

public sealed class ZipInstallerServiceTests
{
    [Fact]
    public async Task InstallFromZipAsync_RejectsHtmlPayloadEvenWhenChecksumMatches()
    {
        using var temp = new TempDir();
        var payload = "<html><body>Your download will start shortly.</body></html>"u8.ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(payload));
        var service = CreateService(payload);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallFromZipAsync(
            "https://example.test/download",
            temp.Combine("tool"),
            ["tool.exe"],
            _ => { },
            progress: null,
            CancellationToken.None,
            checksum));

        Assert.Contains("not a ZIP archive", error.Message);
        Assert.Empty(Directory.EnumerateFiles(temp.Combine("tool")));
    }

    [Fact]
    public async Task InstallFromZipAsync_ExtractsEveryWildcardMatchFlattened()
    {
        using var temp = new TempDir();
        var service = CreateService(CreateZip(
            ("sox-14.4.2/sox.exe", "exe"),
            ("sox-14.4.2/libsox-3.dll", "libsox"),
            ("sox-14.4.2/zlib1.dll", "zlib"),
            ("sox-14.4.2/README.txt", "docs")));
        var target = temp.Combine("sox");

        await service.InstallFromZipAsync(
            "https://example.test/sox.zip",
            target,
            ["sox.exe", "*.dll"],
            _ => { },
            progress: null,
            CancellationToken.None);

        Assert.Equal("exe", File.ReadAllText(Path.Combine(target, "sox.exe")));
        Assert.Equal("libsox", File.ReadAllText(Path.Combine(target, "libsox-3.dll")));
        Assert.Equal("zlib", File.ReadAllText(Path.Combine(target, "zlib1.dll")));
        Assert.False(File.Exists(Path.Combine(target, "README.txt")));
    }

    [Fact]
    public async Task InstallFromZipAsync_MissingRequiredFileDoesNotLeavePartialInstall()
    {
        using var temp = new TempDir();
        var service = CreateService(CreateZip(("package/tool.exe", "exe")));
        var target = temp.Combine("tool");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallFromZipAsync(
            "https://example.test/tool.zip",
            target,
            ["tool.exe", "required.dll"],
            _ => { },
            progress: null,
            CancellationToken.None));

        Assert.Contains("required.dll", error.Message);
        Assert.Empty(Directory.EnumerateFiles(target));
    }

    private static ZipInstallerService CreateService(byte[] payload) =>
        new(new HttpClient(new StaticResponseHandler(payload)));

    private static byte[] CreateZip(params (string Path, string Contents)[] files)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(file.Contents);
            }
        }

        return buffer.ToArray();
    }

    private sealed class StaticResponseHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
                RequestMessage = request,
            });
    }
}
