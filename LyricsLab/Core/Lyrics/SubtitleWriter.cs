using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LyricsLab.Core.Lyrics;

/// <summary>Serializes aligned lines to subtitle formats (SRT, LRC, WebVTT, ASS, SubViewer, MicroDVD, JSON).</summary>
public static class SubtitleWriter
{
    /// <summary>Assumed frame rate for the frame-based MicroDVD format (lyrics have no real video fps).</summary>
    public const double MicroDvdFps = 25.0;

    /// <summary>Standard SubRip (.srt): numbered cues with <c>hh:mm:ss,fff</c> times.</summary>
    public static string ToSrt(IReadOnlyList<AlignedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(SrtTime(lines[i].Start)).Append(" --> ").Append(SrtTime(lines[i].End)).Append('\n');
            sb.Append(lines[i].Text).Append('\n').Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>LRC lyrics: <c>[mm:ss.xx]</c> per line, with optional metadata tags.</summary>
    public static string ToLrc(IReadOnlyList<AlignedLine> lines, SongLyrics? meta = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(meta?.Title)) { sb.Append("[ti:").Append(meta!.Title).Append(']').Append('\n'); }
        if (!string.IsNullOrWhiteSpace(meta?.Artist)) { sb.Append("[ar:").Append(meta!.Artist).Append(']').Append('\n'); }

        foreach (var line in lines)
        {
            sb.Append(LrcTime(line.Start)).Append(line.Text).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// WebVTT (.vtt): web-standard track format compatible with HTML5 &lt;video&gt; and most media players.
    /// Format is identical to SRT except the header is "WEBVTT" and times use a dot instead of a comma.
    /// </summary>
    public static string ToVtt(IReadOnlyList<AlignedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var sb = new StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();
        for (var i = 0; i < lines.Count; i++)
        {
            sb.Append(VttTime(lines[i].Start)).Append(" --> ").AppendLine(VttTime(lines[i].End));
            sb.Append(lines[i].Text).Append('\n').Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Advanced SubStation Alpha (.ass): styled subtitle format with a single default style.
    /// Times use <c>h:mm:ss.cc</c> (centiseconds); literal newlines in a cue become <c>\N</c>.
    /// </summary>
    public static string ToAss(IReadOnlyList<AlignedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var sb = new StringBuilder();

        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine("Collisions: Normal");
        sb.AppendLine("PlayResX: 384");
        sb.AppendLine("PlayResY: 288");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, " +
                      "BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, " +
                      "BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine("Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000," +
                      "0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (var line in lines)
        {
            var text = line.Text.Replace("\r\n", "\n").Replace("\n", "\\N");
            sb.Append("Dialogue: 0,")
              .Append(AssTime(line.Start)).Append(',')
              .Append(AssTime(line.End)).Append(',')
              .Append("Default,,0,0,0,,")
              .Append(text).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// SubViewer / YouTube SBV (.sbv): time-based cues, <c>h:mm:ss.fff,h:mm:ss.fff</c> on one line
    /// followed by the text, blank line between cues. No frame rate needed — fits audio lyrics.
    /// </summary>
    public static string ToSubViewer(IReadOnlyList<AlignedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            sb.Append(SbvTime(lines[i].Start)).Append(',').Append(SbvTime(lines[i].End)).Append('\n');
            sb.Append(lines[i].Text).Append('\n');
            if (i < lines.Count - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// MicroDVD (.sub): frame-based cues <c>{startFrame}{endFrame}text</c>. A leading
    /// <c>{1}{1}fps</c> entry declares the frame rate (<see cref="MicroDvdFps"/>) so players can
    /// convert frames back to time without a video. Multi-line cues use the <c>|</c> separator.
    /// </summary>
    public static string ToMicroDvd(IReadOnlyList<AlignedLine> lines, double fps = MicroDvdFps)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (fps <= 0) fps = MicroDvdFps;

        var sb = new StringBuilder();
        // First entry: declare fps so the frame numbers are interpretable standalone.
        sb.Append("{1}{1}").Append(fps.ToString("0.000", CultureInfo.InvariantCulture)).Append('\n');

        foreach (var line in lines)
        {
            var startFrame = Frame(line.Start, fps);
            var endFrame = Math.Max(Frame(line.End, fps), startFrame + 1);
            var text = line.Text.Replace("\r\n", "\n").Replace("\n", "|");
            sb.Append('{').Append(startFrame).Append("}{").Append(endFrame).Append('}')
              .Append(text).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Machine-readable JSON dump of the cue list: index, start/end (seconds and formatted),
    /// text, section, confidence, and match quality. Not a player format — for re-import and tooling.
    /// </summary>
    public static string ToJson(IReadOnlyList<AlignedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var cues = new object[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            cues[i] = new
            {
                index = i,
                startSeconds = l.Start.TotalSeconds,
                endSeconds = l.End.TotalSeconds,
                start = VttTime(l.Start),
                end = VttTime(l.End),
                text = l.Text,
                section = l.SectionType,
                confidence = l.Confidence.ToString(),
                matchRatio = l.MatchRatio,
                matchedTokens = l.MatchedTokens,
                totalTokens = l.TotalTokens,
            };
        }

        return JsonSerializer.Serialize(
            new { cueCount = lines.Count, cues },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static string SrtTime(TimeSpan t)
    {
        // TimeSpan formatting clamps negatives; guard anyway.
        if (t < TimeSpan.Zero) { t = TimeSpan.Zero; }
        return string.Create(CultureInfo.InvariantCulture,
            $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2},{t.Milliseconds:D3}");
    }

    private static string VttTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return string.Create(CultureInfo.InvariantCulture,
            $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds:D3}");
    }

    private static string LrcTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) { t = TimeSpan.Zero; }
        var hundredths = (int)Math.Round(t.Milliseconds / 10.0);
        var minutes = (int)t.TotalMinutes;
        return string.Create(CultureInfo.InvariantCulture, $"[{minutes:D2}:{t.Seconds:D2}.{hundredths:D2}]");
    }

    private static string AssTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var centi = t.Milliseconds / 10; // ASS uses centiseconds
        return string.Create(CultureInfo.InvariantCulture,
            $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}.{centi:D2}");
    }

    private static string SbvTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return string.Create(CultureInfo.InvariantCulture,
            $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds:D3}");
    }

    private static int Frame(TimeSpan t, double fps)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return (int)Math.Round(t.TotalSeconds * fps);
    }
}
