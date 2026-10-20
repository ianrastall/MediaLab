using System.Xml;
using System.Xml.Linq;

namespace LyricsLab.Core.Lyrics;

/// <summary>
/// Writes a <see cref="SongLyrics"/> object back to the structured XML format, preserving anchors and relative distances.
/// </summary>
public static class LyricsXmlWriter
{
    public static string Write(SongLyrics lyrics)
    {
        ArgumentNullException.ThrowIfNull(lyrics);

        var root = new XElement("song");

        if (!string.IsNullOrWhiteSpace(lyrics.Title))
        {
            root.Add(new XAttribute("title", lyrics.Title));
        }
        if (!string.IsNullOrWhiteSpace(lyrics.Artist))
        {
            root.Add(new XAttribute("artist", lyrics.Artist));
        }
        if (!string.IsNullOrWhiteSpace(lyrics.Year))
        {
            root.Add(new XAttribute("year", lyrics.Year));
        }

        foreach (var section in lyrics.Sections)
        {
            var secElement = new XElement("section");
            secElement.Add(new XAttribute("type", section.Type));

            if (!string.IsNullOrWhiteSpace(section.Id))
            {
                secElement.Add(new XAttribute("id", section.Id));
            }
            if (!string.IsNullOrWhiteSpace(section.Subtype))
            {
                secElement.Add(new XAttribute("subtype", section.Subtype));
            }
            if (section.DurationHint.HasValue)
            {
                secElement.Add(new XAttribute("duration_hint", section.DurationHint.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            if (!string.IsNullOrWhiteSpace(section.Ref))
            {
                secElement.Add(new XAttribute("ref", section.Ref));
            }
            if (section.Occurrence.HasValue)
            {
                secElement.Add(new XAttribute("occurrence", section.Occurrence.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            foreach (var line in section.Lines)
            {
                var lineElement = new XElement("line", line.Text);
                
                if (line.IsAnchor)
                {
                    lineElement.Add(new XAttribute("anchor", "true"));
                }
                
                if (line.DistanceNext.HasValue)
                {
                    lineElement.Add(new XAttribute("distanceNext", line.DistanceNext.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
                
                if (line.Start.HasValue)
                {
                    lineElement.Add(new XAttribute("start", line.Start.Value.ToString("c", System.Globalization.CultureInfo.InvariantCulture)));
                }
                if (line.End.HasValue)
                {
                    lineElement.Add(new XAttribute("end", line.End.Value.ToString("c", System.Globalization.CultureInfo.InvariantCulture)));
                }

                secElement.Add(lineElement);
            }

            root.Add(secElement);
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        
        var settings = new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = false
        };

        using var stringWriter = new System.IO.StringWriter();
        using var xmlWriter = XmlWriter.Create(stringWriter, settings);
        doc.Save(xmlWriter);
        
        return stringWriter.ToString();
    }
}
