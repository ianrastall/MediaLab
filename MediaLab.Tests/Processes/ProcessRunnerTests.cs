using System.IO;
using System.Diagnostics;
using Lab.Core.Processes;
using MediaLab.Tests.TestSupport;

namespace MediaLab.Tests.Processes;

[Trait("Category", "Integration")]
public class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    [Fact]
    public async Task RunAsync_CapturesStdout_AndSucceeds()
    {
        var result = await _runner.RunAsync(RealTools.ComSpec, ["/c", "echo", "hello-medialab"]);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Canceled);
        Assert.Contains("hello-medialab", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_InvokesLineCallbacks()
    {
        var lines = new List<string>();
        await _runner.RunAsync(
            RealTools.ComSpec,
            ["/c", "echo", "line-callback-check"],
            onOutputLine: lines.Add);

        Assert.Contains(lines, l => l.Contains("line-callback-check"));
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_IsNotSuccess()
    {
        var result = await _runner.RunAsync(RealTools.ComSpec, ["/c", "exit", "3"]);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task RunAsync_Cancellation_KillsProcessAndReportsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();

        // ping -n 30 keeps cmd alive ~30s; we cancel after 250ms.
        var runTask = _runner.RunAsync(
            RealTools.ComSpec,
            ["/c", "ping", "-n", "30", "127.0.0.1"],
            captureOutput: false,
            cancellationToken: cts.Token);

        cts.CancelAfter(TimeSpan.FromMilliseconds(250));
        var result = await runTask;
        sw.Stop();

        Assert.True(result.Canceled);
        Assert.False(result.Success);
        // Should return promptly after cancel, nowhere near the full 30s ping.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Took too long: {sw.Elapsed}");
    }

    [Fact]
    public async Task RunAsync_SetsWorkingDirectoryToExecutableFolder_ByDefault()
    {
        // cmd's `cd` with no args prints the current directory; default should be cmd's own folder.
        var result = await _runner.RunAsync(RealTools.ComSpec, ["/c", "cd"]);
        var expectedDir = Path.GetDirectoryName(RealTools.ComSpec)!;

        Assert.Contains(expectedDir, result.StandardOutput.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
