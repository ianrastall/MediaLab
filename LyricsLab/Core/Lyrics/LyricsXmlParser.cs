using System.IO;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace LyricsLab.Core.Lyrics;

/// <summary>
/// Parses the LyricsLab structured-lyrics XML format into a <see cref="SongLyrics"/>. The format
/// preserves line breaks and encodes song structure so the aligner can anchor and skip
/// intelligently. See docs for the schema; unknown attributes are ignored, missing optional
/// attributes become null.
/// </summary>
public static class LyricsXmlParser
{
    /// <summary>Parses XML text. Throws <see cref="FormatException"/> on malformed input.</summary>
    public static SongLyrics Parse(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new FormatException($"Invalid lyrics XML: {ex.Message}", ex);
        }

        var root = doc.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "song", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Lyrics XML must have a <song> root element.");
        }

        var sections = root.Elements()
            .Where(e => string.Equals(e.Name.LocalName, "section", StringComparison.OrdinalIgnoreCase))
            .Select(ParseSection)
            .ToList();

        return new SongLyrics(
            Attr(root, "title"),
            Attr(root, "artist"),
            Attr(root, "year"),
            sections);
    }

    /// <summary>Parses a lyrics XML file from disk.</summary>
    public static SongLyrics ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path));
    }

    private static LyricSection ParseSection(XElement element)
    {
        var lines = element.Elements()
            .Where(e => string.Equals(e.Name.LocalName, "line", StringComparison.OrdinalIgnoreCase))
            .Select(e => new LyricLineData(
                e.Value.Trim(),
                string.Equals(Attr(e, "anchor"), "true", StringComparison.OrdinalIgnoreCase),
                ParseDouble(Attr(e, "distanceNext")),
                ParseTimeSpan(Attr(e, "start")),
                ParseTimeSpan(Attr(e, "end"))
            ))
            .ToList();

        return new LyricSection(
            Type: Attr(element, "type") ?? "unknown",
            Id: Attr(element, "id"),
            Subtype: Attr(element, "subtype"),
            DurationHint: ParseDouble(Attr(element, "duration_hint")),
            Ref: Attr(element, "ref"),
            Occurrence: ParseInt(Attr(element, "occurrence")),
            Lines: lines);
    }

    private static string? Attr(XElement element, string name) =>
        element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static TimeSpan? ParseTimeSpan(string? value) =>
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var ts) ? ts : null;
}
