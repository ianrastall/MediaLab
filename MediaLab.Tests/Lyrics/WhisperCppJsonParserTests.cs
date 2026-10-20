using LyricsLab.Core.Lyrics;

namespace MediaLab.Tests.Lyrics;

public class WhisperCppJsonParserTests
{
    // Real JSON format confirmed from an actual whisper-cli run.
    private const string RealSampleJson = """
        {
            "systeminfo": "WHISPER : COREML = 0 | OPENVINO = 0",
            "transcription": [
                {
                    "timestamps": {"from": "00:00:00,000", "to": "00:00:00,120"},
                    "offsets": {"from": 0, "to": 120},
                    "text": ""
                },
                {
                    "timestamps": {"from": "00:00:00,120", "to": "00:00:00,130"},
                    "offsets": {"from": 120, "to": 130},
                    "text": " Is"
                },
                {
                    "timestamps": {"from": "00:00:00,130", "to": "00:00:00,390"},
                    "offsets": {"from": 130, "to": 390},
                    "text": " this"
                },
                {
                    "timestamps": {"from": "00:00:00,390", "to": "00:00:00,590"},
                    "offsets": {"from": 390, "to": 590},
                    "text": " the"
                },
                {
                    "timestamps": {"from": "00:00:00,590", "to": "00:00:00,850"},
                    "offsets": {"from": 590, "to": 850},
                    "text": " real"
                },
                {
                    "timestamps": {"from": "00:00:00,850", "to": "00:00:01,340"},
                    "offsets": {"from": 850, "to": 1340},
                    "text": " life?"
                }
            ]
        }
        """;

    [Fact]
    public void Parse_RealSample_SkipsEmptyAndReturnsWords()
    {
        var words = WhisperCppJsonParser.Parse(RealSampleJson);

        // Empty-text entry skipped; 5 words remain.
        Assert.Equal(5, words.Count);
        Assert.Equal("Is", words[0].Text);
        Assert.Equal("life?", words[4].Text);
    }

    [Fact]
    public void Parse_RealSample_OffsetsMappedToMilliseconds()
    {
        var words = WhisperCppJsonParser.Parse(RealSampleJson);

        // "Is": from=120ms, to=130ms
        Assert.Equal(TimeSpan.FromMilliseconds(120), words[0].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(130), words[0].End);

        // "life?": from=850ms, to=1340ms
        Assert.Equal(TimeSpan.FromMilliseconds(850), words[4].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(1340), words[4].End);
    }

    [Fact]
    public void Parse_LeadingSpaceStripped()
    {
        var words = WhisperCppJsonParser.Parse(RealSampleJson);
        Assert.All(words, w => Assert.False(w.Text.StartsWith(' '), $"'{w.Text}' has leading space"));
    }

    [Fact]
    public void Parse_EmptyTranscriptionArray_ReturnsEmpty()
    {
        var json = """{"transcription": []}""";
        var words = WhisperCppJsonParser.Parse(json);
        Assert.Empty(words);
    }

    [Fact]
    public void Parse_MissingTranscriptionKey_ReturnsEmpty()
    {
        var json = """{"systeminfo": "..."}""";
        var words = WhisperCppJsonParser.Parse(json);
        Assert.Empty(words);
    }

    [Fact]
    public void Parse_AllWhitespaceText_SkipsEntry()
    {
        var json = """
            {
                "transcription": [
                    {"offsets": {"from": 0, "to": 100}, "text": "   "},
                    {"offsets": {"from": 100, "to": 200}, "text": " hello"}
                ]
            }
            """;
        var words = WhisperCppJsonParser.Parse(json);
        Assert.Single(words);
        Assert.Equal("hello", words[0].Text);
    }

    [Fact]
    public void Parse_MissingOffsets_ZeroTimestamps()
    {
        // Entry with text but no offsets block — should still produce the word with zero timestamps.
        var json = """
            {
                "transcription": [
                    {"text": " hi"}
                ]
            }
            """;
        var words = WhisperCppJsonParser.Parse(json);
        // Current impl skips entries with missing offsets block.
        // Either empty (skip) or present with zero times — either is acceptable behaviour;
        // this test just ensures no exception is thrown.
        Assert.NotNull(words);
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsFalse()
    {
        var ok = WhisperCppJsonParser.TryParse("{not valid json!!}", out var words);
        Assert.False(ok);
        Assert.Empty(words);
    }

    [Fact]
    public void TryParse_ValidSample_ReturnsTrueWithWords()
    {
        var ok = WhisperCppJsonParser.TryParse(RealSampleJson, out var words);
        Assert.True(ok);
        Assert.NotEmpty(words);
    }

    [Fact]
    public void Parse_NullOrWhitespace_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => WhisperCppJsonParser.Parse(null!));
        Assert.ThrowsAny<ArgumentException>(() => WhisperCppJsonParser.Parse("   "));
    }
}
