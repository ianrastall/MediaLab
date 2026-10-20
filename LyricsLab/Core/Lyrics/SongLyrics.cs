namespace LyricsLab.Core.Lyrics;

/// <summary>
/// One section of a song's official lyrics (verse, chorus, instrumental, ...). Section structure
/// is what makes alignment robust: it lets the aligner anchor on known blocks and skip
/// non-vocal stretches rather than fighting transcription errors and repetitions.
/// </summary>
/// <param name="Type">Section kind: intro, verse, chorus, bridge, outro, instrumental, ... (user-extensible).</param>
/// <param name="Id">Optional id for referencing (e.g. "chorus1").</param>
/// <param name="Subtype">Optional refinement (e.g. "guitar_solo").</param>
/// <param name="DurationHint">Optional seconds hint, mainly for instrumental skipping.</param>
/// <param name="Ref">If set, this section repeats a previously-defined section by its id.</param>
/// <param name="Occurrence">Optional 1-based occurrence index for a repeated section.</param>
/// <param name="Lines">The lyric lines, in order (empty for instrumental or ref-only sections).</param>
public sealed record LyricSection(
    string Type,
    string? Id,
    string? Subtype,
    double? DurationHint,
    string? Ref,
    int? Occurrence,
    IReadOnlyList<LyricLineData> Lines)
{
    /// <summary>True for non-vocal sections the aligner should skip in the word stream.</summary>
    public bool IsInstrumental => string.Equals(Type, "instrumental", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when this section only references another (no inline lines of its own).</summary>
    public bool IsReference => !string.IsNullOrWhiteSpace(Ref);
}

/// <summary>Represents a single line's content and alignment metadata.</summary>
public sealed record LyricLineData(string Text, bool IsAnchor, double? DistanceNext, TimeSpan? Start = null, TimeSpan? End = null);

/// <summary>A single vocal lyric line flattened out of the section tree, with its origin.</summary>
public readonly record struct FlatLyricLine(int SectionIndex, string SectionType, int LineIndexInSection, LyricLineData Data);

/// <summary>A song's official, structured lyrics parsed from the LyricsLab XML format.</summary>
public sealed record SongLyrics(
    string? Title,
    string? Artist,
    string? Year,
    IReadOnlyList<LyricSection> Sections)
{
    /// <summary>
    /// All vocal lines in temporal order (instrumental and empty sections skipped), each tagged
    /// with the section it came from. This is the sequence the aligner times against the audio.
    /// </summary>
    public IReadOnlyList<FlatLyricLine> VocalLines()
    {
        var result = new List<FlatLyricLine>();
        var idToSection = new Dictionary<string, LyricSection>(StringComparer.OrdinalIgnoreCase);
        foreach (var sec in Sections)
        {
            if (!string.IsNullOrWhiteSpace(sec.Id) && !sec.IsReference)
            {
                idToSection[sec.Id] = sec;
            }
        }

        for (var s = 0; s < Sections.Count; s++)
        {
            var section = Sections[s];
            if (section.IsInstrumental)
            {
                continue;
            }

            var lines = section.Lines;
            if (section.IsReference && !string.IsNullOrWhiteSpace(section.Ref) && idToSection.TryGetValue(section.Ref, out var refSec))
            {
                lines = refSec.Lines;
            }

            for (var l = 0; l < lines.Count; l++)
            {
                result.Add(new FlatLyricLine(s, section.Type, l, lines[l]));
            }
        }

        return result;
    }
}
