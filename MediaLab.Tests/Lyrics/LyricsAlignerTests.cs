using LyricsLab.Core.Lyrics;

namespace MediaLab.Tests.Lyrics;

public class LyricsAlignerTests
{
    private static TimedWord W(string t, double s, double e) => new(t, TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(e));

    private static List<TimedWord> Words(params (string t, double s, double e)[] xs) =>
        xs.Select(x => W(x.t, x.s, x.e)).ToList();

    private static SongLyrics Song(params (string type, string[] lines)[] sections) =>
        new(null, null, null, sections
            .Select(s => new LyricSection(s.type, null, null, null, null, null, s.lines.Select(l => new LyricLineData(l, false, null)).ToList()))
            .ToList());

    [Fact]
    public void Align_PerfectMatch_AssignsWordSpansPerLine()
    {
        var song = Song(("verse", ["Is this the real life", "Is this just fantasy"]));
        var transcript = Words(
            ("Is", 0, 0.5), ("this", 0.5, 1), ("the", 1, 1.5), ("real", 1.5, 2), ("life", 2, 2.5),
            ("Is", 2.5, 3), ("this", 3, 3.5), ("just", 3.5, 4), ("fantasy", 4, 4.5));

        var aligned = LyricsAligner.Align(song, transcript);

        Assert.Equal(2, aligned.Count);
        Assert.Equal(0.0, aligned[0].Start.TotalSeconds, 2);
        Assert.Equal(2.5, aligned[0].End.TotalSeconds, 2);
        Assert.Equal(2.5, aligned[1].Start.TotalSeconds, 2);
        Assert.Equal(4.5, aligned[1].End.TotalSeconds, 2);
        Assert.All(aligned, l => Assert.Equal(AlignmentConfidence.High, l.Confidence));
    }

    [Fact]
    public void Align_ToleratesInsertedWords()
    {
        // Transcriber adds "um" that isn't in the lyrics.
        var song = Song(("verse", ["hello world"]));
        var transcript = Words(("hello", 0, 1), ("um", 1, 1.4), ("world", 1.4, 2));

        var aligned = LyricsAligner.Align(song, transcript);

        Assert.Single(aligned);
        Assert.Equal(0.0, aligned[0].Start.TotalSeconds, 2);
        Assert.Equal(2.0, aligned[0].End.TotalSeconds, 2);
        Assert.Equal(AlignmentConfidence.High, aligned[0].Confidence);
    }

    [Fact]
    public void Align_ToleratesMissedWords_LowersRatio()
    {
        // Transcriber misses "the"; line still anchored by the other four words.
        var song = Song(("verse", ["Is this the real life"]));
        var transcript = Words(("Is", 0, 0.5), ("this", 0.5, 1), ("real", 1.5, 2), ("life", 2, 2.5));

        var aligned = LyricsAligner.Align(song, transcript);

        Assert.Equal(0.0, aligned[0].Start.TotalSeconds, 2);
        Assert.Equal(2.5, aligned[0].End.TotalSeconds, 2);
        Assert.Equal(0.8, aligned[0].MatchRatio, 2);   // 4 of 5 tokens matched
    }

    [Fact]
    public void Align_InterpolatesLineWithNoMatches_BetweenNeighbors()
    {
        var song = Song(("verse", ["hello world", "mystery middle", "goodbye now"]));
        var transcript = Words(
            ("hello", 0, 1), ("world", 1, 2),
            ("goodbye", 10, 11), ("now", 11, 12));

        var aligned = LyricsAligner.Align(song, transcript);

        Assert.Equal(3, aligned.Count);
        Assert.Equal(AlignmentConfidence.Low, aligned[1].Confidence);
        // Middle line lands strictly between its anchored neighbors.
        Assert.True(aligned[1].Start >= aligned[0].End);
        Assert.True(aligned[1].End <= aligned[2].Start);
    }

    [Fact]
    public void Align_SkipsInstrumentalSection()
    {
        var song = Song(
            ("verse", ["line one"]),
            ("instrumental", []),
            ("verse", ["line two"]));
        var transcript = Words(("line", 0, 0.5), ("one", 0.5, 1), ("line", 10, 10.5), ("two", 10.5, 11));

        var aligned = LyricsAligner.Align(song, transcript);

        Assert.Equal(2, aligned.Count);
        Assert.Equal(0.0, aligned[0].Start.TotalSeconds, 2);
        Assert.Equal(10.0, aligned[1].Start.TotalSeconds, 2);
    }

    [Fact]
    public void Align_RepeatedChorus_MatchesEachOccurrenceInOrder()
    {
        var song = Song(
            ("chorus", ["we are the champions"]),
            ("verse", ["no time for losers"]),
            ("chorus", ["we are the champions"]));
        var transcript = Words(
            ("we", 0, 0.5), ("are", 0.5, 1), ("the", 1, 1.5), ("champions", 1.5, 2),
            ("no", 5, 5.5), ("time", 5.5, 6), ("for", 6, 6.5), ("losers", 6.5, 7),
            ("we", 10, 10.5), ("are", 10.5, 11), ("the", 11, 11.5), ("champions", 11.5, 12));

        var aligned = LyricsAligner.Align(song, transcript);

        Assert.Equal(3, aligned.Count);
        Assert.Equal(0.0, aligned[0].Start.TotalSeconds, 2);   // first chorus
        Assert.Equal(10.0, aligned[2].Start.TotalSeconds, 2);  // second chorus, later in time
        Assert.True(aligned[1].Start >= aligned[0].End && aligned[1].End <= aligned[2].Start);
    }

    [Fact]
    public void Align_EmptyTranscript_DistributesAcrossTotalDuration()
    {
        var song = Song(("verse", ["line a", "line b"]));
        var aligned = LyricsAligner.Align(song, [], new AlignerOptions { TotalDuration = TimeSpan.FromSeconds(10) });

        Assert.Equal(2, aligned.Count);
        Assert.Equal(0.0, aligned[0].Start.TotalSeconds, 1);
        Assert.Equal(5.0, aligned[0].End.TotalSeconds, 1);
        Assert.Equal(10.0, aligned[1].End.TotalSeconds, 1);
        Assert.All(aligned, l => Assert.Equal(AlignmentConfidence.Low, l.Confidence));
    }

    [Fact]
    public void Align_EmptyLyrics_ReturnsEmpty()
    {
        Assert.Empty(LyricsAligner.Align(new SongLyrics(null, null, null, []), Words(("x", 0, 1))));
    }

    [Fact]
    public void Align_OutputIsMonotonicAndNonOverlapping()
    {
        var song = Song(("verse", ["a a a", "b b b", "c c c"]));
        var transcript = Words(("a", 0, 1), ("b", 1, 2), ("c", 2, 3));
        var aligned = LyricsAligner.Align(song, transcript);

        for (var i = 1; i < aligned.Count; i++)
        {
            Assert.True(aligned[i].Start >= aligned[i - 1].End, $"line {i} overlaps previous");
            Assert.True(aligned[i].End > aligned[i].Start, $"line {i} has non-positive duration");
        }
    }

    [Theory]
    [InlineData("Life?", "life")]
    [InlineData("Don't", "dont")]
    [InlineData("  Hello!  ", "hello")]
    [InlineData("123", "123")]
    public void Normalize_KeepsLettersDigitsLowercased(string input, string expected) =>
        Assert.Equal(expected, LyricsAligner.Normalize(input));

    [Fact]
    public void Similarity_ExactIsOne_CloseIsHigh_DisjointIsLow()
    {
        Assert.Equal(1.0, LyricsAligner.Similarity("life", "life"), 3);
        Assert.Equal(0.75, LyricsAligner.Similarity("life", "wife"), 3); // 1 edit / 4
        Assert.Equal(0.0, LyricsAligner.Similarity("life", ""), 3);
    }
}
