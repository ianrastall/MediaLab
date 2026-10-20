using LyricsLab.Core.Lyrics;

namespace MediaLab.Tests.Lyrics;

public class TimingIssueDetectorTests
{
    /// <summary>
    /// Builds an aligned cue with full control. <paramref name="total"/> = 0 models a cue that did
    /// not come from alignment (raw word-grouping), which carries no token counts.
    /// </summary>
    private static AlignedLine Cue(
        double start, double end,
        int matched = 1, int total = 1,
        AlignmentConfidence confidence = AlignmentConfidence.High,
        string text = "line", string section = "verse")
    {
        var ratio = total == 0 ? 1.0 : matched / (double)total;
        return new AlignedLine(text, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end),
            ratio, confidence, section)
        {
            MatchedTokens = matched,
            TotalTokens = total,
        };
    }

    private static List<AlignedLine> Cues(params AlignedLine[] cues) => [.. cues];

    private static bool Has(IReadOnlyList<TimingIssue> issues, TimingIssueCode code, int? index) =>
        issues.Any(i => i.Code == code && i.CueIndex == index);

    // ── nothing wrong ──

    [Fact]
    public void Detect_CleanCues_NoIssues()
    {
        var cues = Cues(
            Cue(0, 2.5, 5, 5),
            Cue(2.5, 5.0, 4, 4),
            Cue(5.0, 7.0, 3, 3));

        Assert.Empty(TimingIssueDetector.Detect(cues));
    }

    [Fact]
    public void Detect_EmptyList_NoIssues() =>
        Assert.Empty(TimingIssueDetector.Detect([]));

    [Fact]
    public void Detect_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => TimingIssueDetector.Detect(null!));

    // ── match quality ──

    [Fact]
    public void Detect_NoMatchedWords_FlagsNoAnchor_NotLowMatch()
    {
        // A fully-interpolated line: tokens exist, none matched. Confidence is Low, but NoAnchorWords
        // is the more specific diagnosis and should win.
        var cues = Cues(Cue(0, 2, matched: 0, total: 4, confidence: AlignmentConfidence.Low));
        var issues = TimingIssueDetector.Detect(cues);

        Assert.True(Has(issues, TimingIssueCode.NoAnchorWords, 0));
        Assert.False(Has(issues, TimingIssueCode.LowMatch, 0));
        Assert.Equal(TimingIssueSeverity.Warning, issues.Single(i => i.Code == TimingIssueCode.NoAnchorWords).Severity);
    }

    [Fact]
    public void Detect_LowConfidence_FlagsLowMatch_WithCounts()
    {
        var cues = Cues(Cue(0, 2, matched: 1, total: 5, confidence: AlignmentConfidence.Low));
        var issues = TimingIssueDetector.Detect(cues);

        var low = issues.Single(i => i.Code == TimingIssueCode.LowMatch);
        Assert.Equal(TimingIssueSeverity.Warning, low.Severity);
        Assert.Equal(0, low.CueIndex);
        Assert.Contains("1/5", low.Message);   // uses the real token counts, not a bare percentage
    }

    [Fact]
    public void Detect_GroupingCue_NoTokenCounts_NoMatchIssue()
    {
        // total == 0 models a raw word-grouping cue: high confidence, no counts. Span checks still
        // apply, but no match-quality issue should be raised.
        var cues = Cues(Cue(0, 2, matched: 0, total: 0, confidence: AlignmentConfidence.High));
        var issues = TimingIssueDetector.Detect(cues);

        Assert.False(Has(issues, TimingIssueCode.NoAnchorWords, 0));
        Assert.False(Has(issues, TimingIssueCode.LowMatch, 0));
    }

    // ── duration ──

    [Fact]
    public void Detect_NonPositiveDuration_Flagged()
    {
        var zero = TimingIssueDetector.Detect(Cues(Cue(5, 5)));
        var inverted = TimingIssueDetector.Detect(Cues(Cue(3, 2.5)));

        Assert.True(Has(zero, TimingIssueCode.NonPositiveDuration, 0));
        Assert.True(Has(inverted, TimingIssueCode.NonPositiveDuration, 0));
        Assert.Equal(TimingIssueSeverity.Warning, zero.Single().Severity);
    }

    [Fact]
    public void Detect_LongCue_FlaggedAsInfo()
    {
        var issues = TimingIssueDetector.Detect(Cues(Cue(0, 15)));   // 15s > 12s default
        var longCue = issues.Single(i => i.Code == TimingIssueCode.LongCue);
        Assert.Equal(TimingIssueSeverity.Info, longCue.Severity);
    }

    [Fact]
    public void Detect_ShortCue_FlaggedAsInfo()
    {
        var issues = TimingIssueDetector.Detect(Cues(Cue(0, 0.2)));  // 0.2s < 0.35s default
        var shortCue = issues.Single(i => i.Code == TimingIssueCode.ShortCue);
        Assert.Equal(TimingIssueSeverity.Info, shortCue.Severity);
    }

    // ── relationship to previous cue ──

    [Fact]
    public void Detect_Overlap_FlaggedOnLaterCue()
    {
        var cues = Cues(Cue(0, 2.0), Cue(1.5, 3.0));   // second starts 0.5s before first ends
        var issues = TimingIssueDetector.Detect(cues);

        Assert.True(Has(issues, TimingIssueCode.Overlap, 1));
        Assert.Equal(TimingIssueSeverity.Warning, issues.Single(i => i.Code == TimingIssueCode.Overlap).Severity);
    }

    [Fact]
    public void Detect_TinyOverlapWithinTolerance_NotFlagged()
    {
        var cues = Cues(Cue(0, 1.0), Cue(0.99, 2.0));  // 10ms overlap < 20ms default tolerance
        Assert.False(Has(TimingIssueDetector.Detect(cues), TimingIssueCode.Overlap, 1));
    }

    [Fact]
    public void Detect_LongGap_FlaggedAsInfo()
    {
        var cues = Cues(Cue(0, 1.0), Cue(12.0, 13.0)); // 11s gap > 10s default
        var issues = TimingIssueDetector.Detect(cues);

        var gap = issues.Single(i => i.Code == TimingIssueCode.LongGap);
        Assert.Equal(1, gap.CueIndex);
        Assert.Equal(TimingIssueSeverity.Info, gap.Severity);
    }

    [Fact]
    public void Detect_NormalGap_NotFlagged()
    {
        var cues = Cues(Cue(0, 1.0), Cue(3.0, 4.0));   // 2s gap, well under threshold
        Assert.False(Has(TimingIssueDetector.Detect(cues), TimingIssueCode.LongGap, 1));
    }

    // ── independence: one cue can collect multiple issues ──

    [Fact]
    public void Detect_OneCue_CanHaveSeveralIndependentIssues()
    {
        // Second cue both overlaps the first and has a weak match — both should be reported.
        var cues = Cues(
            Cue(0, 2.0),
            Cue(1.0, 1.6, matched: 1, total: 5, confidence: AlignmentConfidence.Low));
        var issues = TimingIssueDetector.Detect(cues);

        Assert.True(Has(issues, TimingIssueCode.Overlap, 1));
        Assert.True(Has(issues, TimingIssueCode.LowMatch, 1));
    }

    // ── options ──

    [Fact]
    public void Detect_RespectsCustomThresholds()
    {
        var cues = Cues(Cue(0, 1.0), Cue(4.0, 5.0));   // 3s gap

        Assert.False(Has(TimingIssueDetector.Detect(cues), TimingIssueCode.LongGap, 1));
        Assert.True(Has(
            TimingIssueDetector.Detect(cues, new TimingIssueOptions { LongGap = TimeSpan.FromSeconds(2) }),
            TimingIssueCode.LongGap, 1));
    }
}
