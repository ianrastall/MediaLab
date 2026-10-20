using System.Text.Json;

namespace LyricsLab.Core.Lyrics;

/// <summary>
/// Parses whisper.cpp (<c>whisper-cli -oj</c>) JSON into <see cref="TimedWord"/>s. The format is a
/// root object with a <c>transcription</c> array; each entry has <c>offsets:{from,to}</c> in
/// <b>milliseconds</b> and a <c>text</c> field (leading space, occasionally empty). Run whisper-cli
/// with <c>-ml 1 -sow</c> so each entry is a single word.
/// </summary>
public static class WhisperCppJsonParser
{
    /// <summary>Parses the JSON; entries with empty text are skipped. Throws on malformed JSON.</summary>
    public static IReadOnlyList<TimedWord> Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var words = new List<TimedWord>();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("transcription", out var transcription)
            || transcription.ValueKind != JsonValueKind.Array)
        {
            return words;
        }

        foreach (var entry in transcription.EnumerateArray())
        {
            var text = (entry.TryGetProperty("text", out var t) ? t.GetString() : null)?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (!entry.TryGetProperty("offsets", out var offsets) || offsets.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var from = offsets.TryGetProperty("from", out var f) && f.TryGetInt64(out var fv) ? fv : 0;
            var to = offsets.TryGetProperty("to", out var to2) && to2.TryGetInt64(out var tv) ? tv : from;

            words.Add(new TimedWord(text, TimeSpan.FromMilliseconds(from), TimeSpan.FromMilliseconds(to)));
        }

        return words;
    }

    /// <summary>Non-throwing parse; returns false on malformed JSON or no words.</summary>
    public static bool TryParse(string json, out IReadOnlyList<TimedWord> words)
    {
        try
        {
            words = Parse(json);
            return words.Count > 0;
        }
        catch (JsonException)
        {
            words = [];
            return false;
        }
    }
}
