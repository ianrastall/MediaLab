namespace LyricsLab.Core.Lyrics;

/// <summary>Thresholds for <see cref="TimingIssueDetector"/>. Defaults are tuned for sung lyrics.</summary>
public sealed record TimingIssueOptions
{
    /// <summary>Silent gap before a cue above which we flag a possible instrumental break.</summary>
    public TimeSpan LongGap { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Single-cue duration above which we flag an unusually long cue.</summary>
    public TimeSpan LongCue { get; init; } = TimeSpan.FromSeconds(12);

    /// <summary>Single-cue duration below which we flag an unusually short cue.</summary>
    public TimeSpan ShortCue { get; init; } = TimeSpan.FromSeconds(0.35);

    /// <summary>How far two cues may overlap before it counts (absorbs rounding noise).</summary>
    public TimeSpan OverlapTolerance { get; init; } = TimeSpan.FromMilliseconds(20);
}

/// <summary>
/// Derives a human-readable issue list from an aligned cue list.
///
/// <para>Pure and side-effect-free, so the GUI can re-run it cheaply after the user hand-edits a
/// time. The match-quality checks (<see cref="TimingIssueCode.NoAnchorWords"/>,
/// <see cref="TimingIssueCode.LowMatch"/>) rely on data the aligner already produces — the per-line
/// match ratio and token counts — rather than recomputing anything. The span checks (gap, overlap,
/// duration) are computed straight from cue start/end times, so they also catch problems the user
/// introduces by editing, not just ones the aligner left behind.</para>
///
/// <para>Cues that did not come from alignment (e.g. raw word-grouping) carry no token counts and a
/// match ratio of 1.0; the match-quality checks deliberately skip them, while the span checks still
/// apply.</para>
/// </summary>
public static class TimingIssueDetector
{
    public static IReadOnlyList<TimingIssue> Detect(
        IReadOnlyList<AlignedLine> cues, TimingIssueOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cues);
        options ??= new TimingIssueOptions();

        var issues = new List<TimingIssue>();

        for (var i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];

            // ----- match quality (aligned cues only) -----
            if (cue.TotalTokens > 0 && cue.MatchedTokens == 0)
            {
                issues.Add(new TimingIssue(
                    TimingIssueCode.NoAnchorWords, TimingIssueSeverity.Warning, i,
                    "No words matched — timing is interpolated from neighbours."));
            }
            else if (cue.Confidence == AlignmentConfidence.Low)
            {
                issues.Add(new TimingIssue(
                    TimingIssueCode.LowMatch, TimingIssueSeverity.Warning, i,
                    $"Weak match — {MatchPhrase(cue)}."));
            }

            // ----- duration (independent of match quality) -----
            var duration = cue.End - cue.Start;
            if (duration <= TimeSpan.Zero)
            {
                issues.Add(new TimingIssue(
                    TimingIssueCode.NonPositiveDuration, TimingIssueSeverity.Warning, i,
                    "End time is at or before the start time."));
            }
            else if (duration > options.LongCue)
            {
                issues.Add(new TimingIssue(
                    TimingIssueCode.LongCue, TimingIssueSeverity.Info, i,
                    $"Unusually long cue ({duration.TotalSeconds:n0}s) — two lines merged?"));
            }
            else if (duration < options.ShortCue)
            {
                issues.Add(new TimingIssue(
                    TimingIssueCode.ShortCue, TimingIssueSeverity.Info, i,
                    $"Very short cue ({duration.TotalSeconds:n2}s)."));
            }

            // ----- relationship to the previous cue -----
            if (i > 0)
            {
                var prevEnd = cues[i - 1].End;

                if (cue.Start + options.OverlapTolerance < prevEnd)
                {
                    issues.Add(new TimingIssue(
                        TimingIssueCode.Overlap, TimingIssueSeverity.Warning, i,
                        "Starts before the previous cue ends."));
                }
                else
                {
                    var gap = cue.Start - prevEnd;
                    if (gap > options.LongGap)
                    {
                        issues.Add(new TimingIssue(
                            TimingIssueCode.LongGap, TimingIssueSeverity.Info, i,
                            $"Long gap before this line ({gap.TotalSeconds:n0}s) — possible instrumental."));
                    }
                }
            }
        }

        return issues;
    }

    private static string MatchPhrase(AlignedLine cue) =>
        cue.TotalTokens > 0
            ? $"{cue.MatchedTokens}/{cue.TotalTokens} words matched"
            : $"{cue.MatchRatio:P0} of words matched";
}
