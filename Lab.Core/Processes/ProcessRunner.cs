using System.Diagnostics;
using System.Text;

namespace Lab.Core.Processes;

/// <summary>
/// Runs child CLI tools (ffmpeg, ffprobe, SoX, ...) with safe defaults for the suite:
/// <list type="bullet">
///   <item>arguments are passed via <see cref="ProcessStartInfo.ArgumentList"/>, never a shell-quoted string;</item>
///   <item>working directory defaults to the executable's own folder so adjacent DLLs resolve;</item>
///   <item>stdout/stderr are streamed line-by-line to optional callbacks;</item>
///   <item>cancellation kills the whole process tree.</item>
/// </list>
/// Full-output capture is opt-out: long-running tools like ffmpeg should disable it and
/// rely on the line callbacks plus a bounded log buffer so UI memory stays bounded.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>
    /// Starts <paramref name="fileName"/> and waits for it to exit.
    /// </summary>
    /// <param name="fileName">Full path to the executable.</param>
    /// <param name="arguments">Arguments, one element per token (no manual quoting).</param>
    /// <param name="workingDirectory">
    /// Working directory; defaults to the executable's directory so adjacent native DLLs load.
    /// </param>
    /// <param name="onOutputLine">Invoked for each stdout line (off the UI thread).</param>
    /// <param name="onErrorLine">Invoked for each stderr line (off the UI thread).</param>
    /// <param name="captureOutput">
    /// When true, stdout/stderr are also accumulated into the returned <see cref="ProcessResult"/>.
    /// Set false for tools that emit unbounded output.
    /// </param>
    /// <param name="cancellationToken">Cancelling kills the process tree and returns a cancelled result.</param>
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        bool captureOutput = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(Path.GetFullPath(fileName)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {fileName}");
        }

        // Tie the child's lifetime to ours: if this app crashes or is force-closed mid-render,
        // the OS kills the child (and any process it spawns, e.g. a Chocolatey shim's real exe)
        // instead of leaving it running orphaned. Cooperative cancellation below still applies
        // for the normal case; this is the backstop for when that path never gets to run.
        if (OperatingSystem.IsWindows())
        {
            JobObject.Assign(process);
        }

        // Drain both streams concurrently so a full pipe can never deadlock the child.
        var stdoutTask = PumpAsync(process.StandardOutput, onOutputLine, captureOutput ? stdout : null, cancellationToken);
        var stderrTask = PumpAsync(process.StandardError, onErrorLine, captureOutput ? stderr : null, cancellationToken);

        var canceled = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            KillTree(process);
            // Wait (uncancellably) for the now-killed process to actually exit so exit code is valid.
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* already gone */ }
        }

        // Let the pumps finish draining whatever remained after exit.
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        stopwatch.Stop();

        // If cancellation was requested right as the process exited cleanly, honor the cancel
        // rather than reporting success — the user's intent was to stop.
        if (!canceled && cancellationToken.IsCancellationRequested)
        {
            canceled = true;
        }

        var exitCode = canceled ? -1 : SafeExitCode(process);
        return new ProcessResult(exitCode, stdout.ToString(), stderr.ToString(), stopwatch.Elapsed, canceled);
    }

    private static async Task PumpAsync(
        StreamReader reader,
        Action<string>? onLine,
        StringBuilder? capture,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                onLine?.Invoke(line);
                capture?.AppendLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation tears the pipe down; whatever we captured so far is kept.
        }
        catch (IOException)
        {
            // Pipe closed underneath us when the process was killed.
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between HasExited check and Kill.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied or process is already terminating — treat as best-effort success.
        }
        catch (NotSupportedException)
        {
            // entireProcessTree not supported on this platform.
        }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }
}
