namespace LyricsLab.Core.Lyrics;

/// <summary>
/// Severity of a <see cref="TimingIssue"/>. Two tiers keep the review list signal-rich: a Warning
/// almost always wants a human, an Info is a heads-up that is often fine on inspection.
/// </summary>
public enum TimingIssueSeverity
{
    /// <summary>Worth a glance, not necessarily wrong (long gap, unusually long/short cue).</summary>
    Info,

    /// <summary>Likely needs correction: weak/absent word match, overlap, or non-positive duration.</summary>
    Warning,
}

/// <summary>
/// Stable identifier for a kind of timing problem. Kept as an enum (not a free string) so the GUI
/// can map each kind to an icon/label and so the set is exhaustive at the call site.
/// </summary>
public enum TimingIssueCode
{
    /// <summary>The line has tokens but none matched a transcript word — its timing is pure interpolation.</summary>
    NoAnchorWords,

    /// <summary>Few of the line's words matched (Low confidence) — the span is probably loose.</summary>
    LowMatch,

    /// <summary>This cue starts before the previous cue ends.</summary>
    Overlap,

    /// <summary>End time is at or before the start time.</summary>
    NonPositiveDuration,

    /// <summary>A long silent gap precedes this cue — often an unlabeled instrumental break.</summary>
    LongGap,

    /// <summary>A single cue is unusually long — often two lines merged or a stray end time.</summary>
    LongCue,

    /// <summary>A cue is very short — hard to read at singing speed.</summary>
    ShortCue,
}

/// <summary>
/// One detected problem with an aligned cue list. <see cref="CueIndex"/> is the 0-based index of the
/// cue the issue refers to, or <c>null</c> for a sequence-level issue with no single owning cue.
/// </summary>
/// <param name="Code">Which kind of problem this is.</param>
/// <param name="Severity">How strongly it wants attention.</param>
/// <param name="CueIndex">0-based cue index, or null for a sequence-level issue.</param>
/// <param name="Message">Human-readable, ready to show in the UI.</param>
public sealed record TimingIssue(
    TimingIssueCode Code,
    TimingIssueSeverity Severity,
    int? CueIndex,
    string Message);
