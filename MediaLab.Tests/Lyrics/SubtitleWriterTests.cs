using LyricsLab.Core.Lyrics;

namespace MediaLab.Tests.Lyrics;

public class SubtitleWriterTests
{
    private static AlignedLine Line(string text, double start, double end) =>
        new(text, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), 1.0, AlignmentConfidence.High, "verse");

    [Fact]
    public void ToSrt_ProducesNumberedCuesWithCommaMillis()
    {
        var srt = SubtitleWriter.ToSrt([Line("Is this the real life", 0, 2.5), Line("Is this just fantasy", 2.5, 5)]);

        var expected =
            "1\n00:00:00,000 --> 00:00:02,500\nIs this the real life\n\n" +
            "2\n00:00:02,500 --> 00:00:05,000\nIs this just fantasy\n\n";
        Assert.Equal(expected, srt);
    }

    [Fact]
    public void ToSrt_FormatsHoursMinutesSecondsMillis()
    {
        var srt = SubtitleWriter.ToSrt([Line("late", 3723.456, 3724.0)]);
        Assert.Contains("01:02:03,456 -->", srt);
    }

    [Fact]
    public void ToLrc_EmitsMetadataAndTimestampedLines()
    {
        var meta = new SongLyrics("My Song", "My Artist", "2024", []);
        var lrc = SubtitleWriter.ToLrc([Line("first line", 1.5, 3), Line("second line", 3, 4)], meta);

        Assert.Contains("[ti:My Song]", lrc);
        Assert.Contains("[ar:My Artist]", lrc);
        Assert.Contains("[00:01.50]first line", lrc);
        Assert.Contains("[00:03.00]second line", lrc);
    }

    [Fact]
    public void ToLrc_WithoutMeta_OmitsTags()
    {
        var lrc = SubtitleWriter.ToLrc([Line("solo", 0, 1)]);
        Assert.DoesNotContain("[ti:", lrc);
        Assert.StartsWith("[00:00.00]solo", lrc);
    }

    // ── ASS ──

    [Fact]
    public void ToAss_HasHeaderStyleAndDialogue()
    {
        var ass = SubtitleWriter.ToAss([Line("hello", 1, 2.5)]);

        Assert.Contains("[Script Info]", ass);
        Assert.Contains("[V4+ Styles]", ass);
        Assert.Contains("Style: Default,", ass);
        Assert.Contains("[Events]", ass);
        Assert.Contains("Dialogue: 0,0:00:01.00,0:00:02.50,Default,,0,0,0,,hello", ass);
    }

    [Fact]
    public void ToAss_EscapesNewlinesAsBackslashN()
    {
        var ass = SubtitleWriter.ToAss([Line("line one\nline two", 0, 1)]);
        Assert.Contains(",line one\\Nline two", ass);
    }

    // ── SubViewer (.sbv) ──

    [Fact]
    public void ToSubViewer_TimeBasedCuesSeparatedByBlankLine()
    {
        var sbv = SubtitleWriter.ToSubViewer([Line("first", 0, 2.5), Line("second", 2.5, 5)]);

        var expected =
            "0:00:00.000,0:00:02.500\nfirst\n\n" +
            "0:00:02.500,0:00:05.000\nsecond\n";
        Assert.Equal(expected, sbv);
    }

    // ── MicroDVD (.sub) ──

    [Fact]
    public void ToMicroDvd_DeclaresFpsAndUsesFrameNumbers()
    {
        // At 25 fps: 2.0s → frame 50, 3.0s → frame 75.
        var sub = SubtitleWriter.ToMicroDvd([Line("frameline", 2.0, 3.0)]);

        Assert.StartsWith("{1}{1}25.000\n", sub);
        Assert.Contains("{50}{75}frameline", sub);
    }

    [Fact]
    public void ToMicroDvd_EnsuresEndFrameAfterStart_AndPipesNewlines()
    {
        var sub = SubtitleWriter.ToMicroDvd([Line("a\nb", 1.0, 1.0)]); // zero-length → end forced to start+1
        Assert.Contains("{25}{26}a|b", sub);
    }

    // ── JSON ──

    [Fact]
    public void ToJson_EmitsCueCountAndFields()
    {
        var json = SubtitleWriter.ToJson([Line("hello", 1, 2.5)]);

        Assert.Contains("\"cueCount\": 1", json);
        Assert.Contains("\"text\": \"hello\"", json);
        Assert.Contains("\"section\": \"verse\"", json);
        Assert.Contains("\"confidence\": \"High\"", json);
        Assert.Contains("\"startSeconds\": 1", json);
    }
}
