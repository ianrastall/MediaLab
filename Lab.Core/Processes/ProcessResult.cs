namespace Lab.Core.Processes;

/// <summary>
/// Outcome of a single child-process run.
/// </summary>
/// <param name="ExitCode">Process exit code, or -1 if the process never produced one (e.g. killed).</param>
/// <param name="StandardOutput">Captured stdout, or empty when capture was disabled.</param>
/// <param name="StandardError">Captured stderr, or empty when capture was disabled.</param>
/// <param name="Elapsed">Wall-clock duration from start to exit.</param>
/// <param name="Canceled">True when the run was cancelled via its <see cref="System.Threading.CancellationToken"/>.</param>
public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Elapsed,
    bool Canceled)
{
    /// <summary>A run is successful only if it exited 0 and was not cancelled.</summary>
    public bool Success => ExitCode == 0 && !Canceled;
}
