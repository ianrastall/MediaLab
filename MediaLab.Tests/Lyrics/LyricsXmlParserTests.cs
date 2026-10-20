using LyricsLab.Core.Lyrics;

namespace MediaLab.Tests.Lyrics;

public class LyricsXmlParserTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="UTF-8"?>
        <song title="Bohemian Rhapsody" artist="Queen" year="1975">
          <section type="intro" id="intro1">
            <line>Is this the real life?</line>
            <line>Is this just fantasy?</line>
          </section>
          <section type="verse" id="verse1">
            <line>Mama, just killed a man</line>
            <line>Put a gun against his head</line>
          </section>
          <section type="instrumental" subtype="guitar_solo" duration_hint="12.5" />
          <section type="chorus" id="chorus1">
            <line>This is the chorus</line>
          </section>
          <section type="chorus" ref="chorus1" occurrence="2" />
        </song>
        """;

    [Fact]
    public void Parse_ReadsSongMetadata()
    {
        var song = LyricsXmlParser.Parse(Sample);

        Assert.Equal("Bohemian Rhapsody", song.Title);
        Assert.Equal("Queen", song.Artist);
        Assert.Equal("1975", song.Year);
        Assert.Equal(5, song.Sections.Count);
    }

    [Fact]
    public void Parse_ReadsSectionTypesAndLines()
    {
        var song = LyricsXmlParser.Parse(Sample);

        Assert.Equal("intro", song.Sections[0].Type);
        Assert.Equal("intro1", song.Sections[0].Id);
        Assert.Equal(new[] { "Is this the real life?", "Is this just fantasy?" }, song.Sections[0].Lines.Select(l => l.Text));

        Assert.Equal("verse", song.Sections[1].Type);
        Assert.Equal(2, song.Sections[1].Lines.Count);
    }

    [Fact]
    public void Parse_HandlesInstrumentalSection()
    {
        var song = LyricsXmlParser.Parse(Sample);
        var instrumental = song.Sections[2];

        Assert.True(instrumental.IsInstrumental);
        Assert.Equal("guitar_solo", instrumental.Subtype);
        Assert.Equal(12.5, instrumental.DurationHint);
        Assert.Empty(instrumental.Lines);
    }

    [Fact]
    public void Parse_HandlesReferencedRepeatSection()
    {
        var song = LyricsXmlParser.Parse(Sample);
        var repeat = song.Sections[4];

        Assert.True(repeat.IsReference);
        Assert.Equal("chorus1", repeat.Ref);
        Assert.Equal(2, repeat.Occurrence);
        Assert.Empty(repeat.Lines);
    }

    [Fact]
    public void VocalLines_FlattensInOrder_SkippingInstrumental()
    {
        var song = LyricsXmlParser.Parse(Sample);
        var vocal = song.VocalLines();

        // intro(2) + verse(2) + chorus(1) + chorus_ref(1) = 6
        Assert.Equal(6, vocal.Count);
        Assert.Equal("Is this the real life?", vocal[0].Data.Text);
        Assert.Equal("intro", vocal[0].SectionType);
        Assert.Equal("This is the chorus", vocal[^1].Data.Text);
        Assert.DoesNotContain(vocal, v => v.SectionType == "instrumental");
    }

    [Fact]
    public void Parse_MissingOptionalAttributes_AreNull()
    {
        var song = LyricsXmlParser.Parse("<song><section type=\"verse\"><line>hi</line></section></song>");

        Assert.Null(song.Title);
        var section = Assert.Single(song.Sections);
        Assert.Null(section.Id);
        Assert.Null(section.DurationHint);
        Assert.Null(section.Occurrence);
        Assert.False(section.IsReference);
    }

    [Theory]
    [InlineData("not xml at all <<<")]
    [InlineData("<notsong></notsong>")]
    public void Parse_InvalidInput_Throws(string xml)
    {
        Assert.Throws<FormatException>(() => LyricsXmlParser.Parse(xml));
    }

    [Fact]
    public void Parse_UsesInvariantCultureForDurationHint()
    {
        // A dot decimal must parse regardless of machine culture (never comma).
        var song = LyricsXmlParser.Parse("<song><section type=\"instrumental\" duration_hint=\"3.25\"/></song>");
        Assert.Equal(3.25, song.Sections[0].DurationHint);
    }
}
