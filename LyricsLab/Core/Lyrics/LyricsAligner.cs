using System.Text;

namespace LyricsLab.Core.Lyrics;

/// <summary>How much to trust a line's timing.</summary>
public enum AlignmentConfidence
{
    High,
    Medium,
    Low,
}

/// <summary>An official lyric line with the time window assigned to it.</summary>
public sealed record AlignedLine(
    string Text,
    TimeSpan Start,
    TimeSpan End,
    double MatchRatio,
    AlignmentConfidence Confidence,
    string SectionType)
{
    /// <summary>How many of this line's official tokens matched a transcript word (0 when unknown).</summary>
    public int MatchedTokens { get; init; }

    /// <summary>Total official tokens on this line (0 when the cue did not come from alignment).</summary>
    public int TotalTokens { get; init; }

    /// <summary>"matched/total" when token counts are known, else the match ratio as a percentage.</summary>
    public string MatchSummary => TotalTokens > 0
        ? $"{MatchedTokens}/{TotalTokens}"
        : MatchRatio.ToString("P0", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Tunable knobs for <see cref="LyricsAligner"/>.</summary>
public sealed record AlignerOptions
{
    /// <summary>Minimum token similarity (0–1) to count as a match during traceback.</summary>
    public double MatchThreshold { get; init; } = 0.7;

    /// <summary>Penalty per unmatched token (insertion/deletion) in the DP score.</summary>
    public double GapPenalty { get; init; } = -0.5;

    /// <summary>Minimum duration enforced for every output line.</summary>
    public TimeSpan MinLineDuration { get; init; } = TimeSpan.FromSeconds(0.5);

    /// <summary>Total media duration, used to bound trailing interpolation when known.</summary>
    public TimeSpan? TotalDuration { get; init; }

    /// <summary>Fallback per-line duration when nothing can be anchored.</summary>
    public double DefaultLineSeconds { get; init; } = 2.0;
}

/// <summary>
/// Times official, structured lyrics against a transcript's word timings.
///
/// <para>The official lyric tokens and the transcript tokens are both in temporal order, so a
/// single global <b>token-level dynamic-programming alignment</b> (Needleman–Wunsch with fuzzy
/// token similarity) is order-preserving and naturally absorbs the transcriber's insertions
/// ("um"), deletions (missed words), and expected repetitions (a chorus appearing twice). Each
/// official line then takes the span of its matched transcript words; lines whose words were
/// missed are interpolated between their anchored neighbors and flagged low-confidence.</para>
///
/// <para>Anchoring on unique sections and a DTW fallback for pathologically repetitive songs are
/// documented future robustness layers; global token DP is the v1 core.</para>
/// </summary>
public static class LyricsAligner
{
    public static IReadOnlyList<AlignedLine> Align(
        SongLyrics lyrics,
        IReadOnlyList<TimedWord> transcript,
        AlignerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(transcript);
        options ??= new AlignerOptions();

        var vocalLines = lyrics.VocalLines();
        if (vocalLines.Count == 0)
        {
            return [];
        }

        // Flatten official tokens, remembering which line each belongs to.
        var officialTokens = new List<(int LineIndex, string Norm)>();
        var lineTokenCount = new int[vocalLines.Count];
        for (var v = 0; v < vocalLines.Count; v++)
        {
            foreach (var norm in Tokenize(vocalLines[v].Data.Text))
            {
                officialTokens.Add((v, norm));
                lineTokenCount[v]++;
            }
        }

        // Compute minimum start time based on leading instrumental sections.
        var minStartSec = 0.0;
        foreach (var sec in lyrics.Sections)
        {
            if (sec.IsInstrumental)
            {
                minStartSec += sec.DurationHint ?? 0.0;
            }
            else if (sec.Lines.Count > 0 || sec.IsReference)
            {
                break;
            }
        }
        var safeMinStart = TimeSpan.FromSeconds(Math.Max(0, minStartSec - 1.0)); // 1s buffer

        // Normalize transcript tokens (drop ones that normalize to empty or fall before safeMinStart).
        var transcriptTokens = new List<(TimedWord Word, string Norm)>();
        foreach (var w in transcript)
        {
            if (w.End < safeMinStart)
            {
                continue; // Skip garbage transcribed during the leading instrumental
            }

            var norm = Normalize(w.Text);
            if (norm.Length > 0)
            {
                transcriptTokens.Add((w, norm));
            }
        }

        // Find manual anchors
        var anchorLineIndices = new List<int>();
        for (var i = 0; i < vocalLines.Count; i++)
        {
            var data = vocalLines[i].Data;
            if (data.IsAnchor && data.Start.HasValue && data.End.HasValue)
            {
                anchorLineIndices.Add(i);
            }
        }

        var matchedWord = new TimedWord?[officialTokens.Count];
        int officialCursor = 0;
        
        for (int a = 0; a <= anchorLineIndices.Count; a++)
        {
            int nextAnchorLineIdx = a < anchorLineIndices.Count ? anchorLineIndices[a] : vocalLines.Count;
            
            var segmentOfficialTokens = new List<(int LineIndex, string Norm)>();
            int segmentOfficialStart = officialCursor;
            
            while (officialCursor < officialTokens.Count && officialTokens[officialCursor].LineIndex < nextAnchorLineIdx)
            {
                segmentOfficialTokens.Add(officialTokens[officialCursor]);
                officialCursor++;
            }
            
            double segStartT = a == 0 ? 0.0 : vocalLines[anchorLineIndices[a-1]].Data.End!.Value.TotalSeconds;
            double segEndT = a < anchorLineIndices.Count ? vocalLines[anchorLineIndices[a]].Data.Start!.Value.TotalSeconds : double.MaxValue;
            
            var segmentTranscriptTokens = new List<(TimedWord Word, string Norm)>();
            foreach (var w in transcriptTokens)
            {
                var mid = w.Word.Start.TotalSeconds + (w.Word.End.TotalSeconds - w.Word.Start.TotalSeconds) / 2.0;
                if (mid >= segStartT && mid <= segEndT)
                {
                    segmentTranscriptTokens.Add(w);
                }
            }
            
            var segmentMatched = AlignTokens(segmentOfficialTokens, segmentTranscriptTokens, options);
            
            for (int i = 0; i < segmentMatched.Length; i++)
            {
                matchedWord[segmentOfficialStart + i] = segmentMatched[i];
            }
            
            if (a < anchorLineIndices.Count)
            {
                while (officialCursor < officialTokens.Count && officialTokens[officialCursor].LineIndex == nextAnchorLineIdx)
                {
                    officialCursor++;
                }
            }
        }

        var startSec = new double?[vocalLines.Count];
        var endSec = new double?[vocalLines.Count];
        var ratio = new double[vocalLines.Count];
        var matchedPerLine = new int[vocalLines.Count];

        // Pre-fill anchors so they act as fixed boundaries
        foreach (var idx in anchorLineIndices)
        {
            var data = vocalLines[idx].Data;
            startSec[idx] = data.Start!.Value.TotalSeconds;
            endSec[idx] = data.End!.Value.TotalSeconds;
            ratio[idx] = 1.0;
            matchedPerLine[idx] = lineTokenCount[idx];
        }

        // Apply DP results for unanchored lines
        for (var i = 0; i < officialTokens.Count; i++)
        {
            if (matchedWord[i] is { } word)
            {
                var line = officialTokens[i].LineIndex;
                matchedPerLine[line]++;
                var s = word.Start.TotalSeconds;
                var e = word.End.TotalSeconds;
                startSec[line] = startSec[line] is { } cs ? Math.Min(cs, s) : s;
                endSec[line] = endSec[line] is { } ce ? Math.Max(ce, e) : e;
            }
        }

        var anchorSet = new HashSet<int>(anchorLineIndices);
        for (var v = 0; v < vocalLines.Count; v++)
        {
            if (!anchorSet.Contains(v))
            {
                ratio[v] = lineTokenCount[v] == 0 ? 0 : matchedPerLine[v] / (double)lineTokenCount[v];
            }
        }

        FillUnanchored(startSec, endSec, options);
        return BuildLines(vocalLines, startSec, endSec, ratio, matchedPerLine, lineTokenCount, options);
    }

    // ----- token alignment (Needleman–Wunsch) -----

    private static TimedWord?[] AlignTokens(
        List<(int LineIndex, string Norm)> a,
        List<(TimedWord Word, string Norm)> b,
        AlignerOptions options)
    {
        var matched = new TimedWord?[a.Count];
        if (a.Count == 0 || b.Count == 0)
        {
            return matched;
        }

        var n = a.Count;
        var m = b.Count;
        var dp = new double[n + 1, m + 1];
        var tb = new byte[n + 1, m + 1]; // 0=diag, 1=up (official unmatched), 2=left (transcript unmatched)
        var gap = options.GapPenalty;

        for (var i = 1; i <= n; i++) { dp[i, 0] = i * gap; tb[i, 0] = 1; }
        for (var j = 1; j <= m; j++) { dp[0, j] = j * gap; tb[0, j] = 2; }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var diag = dp[i - 1, j - 1] + Similarity(a[i - 1].Norm, b[j - 1].Norm);
                var up = dp[i - 1, j] + gap;
                var left = dp[i, j - 1] + gap;

                if (diag >= up && diag >= left) { dp[i, j] = diag; tb[i, j] = 0; }
                else if (up >= left) { dp[i, j] = up; tb[i, j] = 1; }
                else { dp[i, j] = left; tb[i, j] = 2; }
            }
        }

        // Traceback; record a match only when paired tokens are similar enough.
        var x = n;
        var y = m;
        while (x > 0 || y > 0)
        {
            var move = tb[x, y];
            if (move == 0)
            {
                if (Similarity(a[x - 1].Norm, b[y - 1].Norm) >= options.MatchThreshold)
                {
                    matched[x - 1] = b[y - 1].Word;
                }

                x--; y--;
            }
            else if (move == 1) { x--; }
            else { y--; }
        }

        return matched;
    }

    // ----- interpolation of lines with no matched words -----

    private static void FillUnanchored(double?[] start, double?[] end, AlignerOptions options)
    {
        var n = start.Length;

        // Bounds for trailing/leading interpolation.
        var firstStart = start.FirstOrDefault(s => s is not null) ?? 0.0;
        var lastEnd = 0.0;
        var anyAnchor = false;
        for (var i = 0; i < n; i++)
        {
            if (end[i] is { } e) { lastEnd = Math.Max(lastEnd, e); anyAnchor = true; }
        }

        if (!anyAnchor)
        {
            // Nothing matched: spread evenly across the known duration, else sequential defaults.
            // Treat TotalDuration of zero (or negative) as unknown — avoid zero-span lines.
            var totalSecs = options.TotalDuration?.TotalSeconds;
            var total = (totalSecs is > 0) ? totalSecs.Value : n * options.DefaultLineSeconds;
            var per = total / n;
            for (var i = 0; i < n; i++) { start[i] = i * per; end[i] = (i + 1) * per; }
            return;
        }

        for (var i = 0; i < n; i++)
        {
            if (start[i] is not null)
            {
                continue;
            }

            // Find the run of consecutive unanchored lines [i, j).
            var j = i;
            while (j < n && start[j] is null)
            {
                j++;
            }

            var lower = i > 0 && end[i - 1] is { } pe ? pe : firstStart;
            var upper = j < n && start[j] is { } ns ? ns
                : (options.TotalDuration?.TotalSeconds is { } td && td > lastEnd ? td : lastEnd + ((j - i) * options.DefaultLineSeconds));
            if (upper < lower) { upper = lower + ((j - i) * options.DefaultLineSeconds); }

            var span = (upper - lower) / (j - i);
            var maxDur = options.DefaultLineSeconds;
            
            if (span > maxDur + 0.5)
            {
                var step = maxDur + 0.5;
                var blockDur = (j - i) * step;
                var pad = Math.Max(0, (upper - lower - blockDur) / 2.0);
                
                for (var k = i; k < j; k++)
                {
                    start[k] = lower + pad + ((k - i) * step);
                    end[k] = start[k] + maxDur;
                }
            }
            else
            {
                for (var k = i; k < j; k++)
                {
                    start[k] = lower + ((k - i) * span);
                    // Leave a tiny gap (0.1s) if possible so they don't visually run together
                    end[k] = Math.Max(start[k]!.Value, lower + ((k - i + 1) * span) - 0.1);
                }
            }

            i = j - 1;
        }
    }

    private static IReadOnlyList<AlignedLine> BuildLines(
        IReadOnlyList<FlatLyricLine> vocal,
        double?[] start,
        double?[] end,
        double[] ratio,
        int[] matchedPerLine,
        int[] lineTokenCount,
        AlignerOptions options)
    {
        var result = new List<AlignedLine>(vocal.Count);
        var minDur = options.MinLineDuration.TotalSeconds;
        var cursor = 0.0;

        for (var v = 0; v < vocal.Count; v++)
        {
            var s = Math.Max(start[v] ?? cursor, cursor);
            var e = Math.Max(end[v] ?? (s + minDur), s + minDur);
            cursor = e;

            result.Add(new AlignedLine(
                vocal[v].Data.Text,
                TimeSpan.FromSeconds(s),
                TimeSpan.FromSeconds(e),
                ratio[v],
                Classify(ratio[v]),
                vocal[v].SectionType)
            {
                MatchedTokens = matchedPerLine[v],
                TotalTokens = lineTokenCount[v],
            });
        }

        return result;
    }

    private static AlignmentConfidence Classify(double ratio) =>
        ratio >= 0.7 ? AlignmentConfidence.High
        : ratio >= 0.4 ? AlignmentConfidence.Medium
        : AlignmentConfidence.Low;

    // ----- text helpers -----

    /// <summary>Splits a line into normalized word tokens (empties dropped).</summary>
    internal static IEnumerable<string> Tokenize(string line)
    {
        foreach (var raw in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var norm = Normalize(raw);
            if (norm.Length > 0)
            {
                yield return norm;
            }
        }
    }

    /// <summary>Lower-cases and keeps only letters/digits, so "Life?" and "life" match.</summary>
    internal static string Normalize(string token)
    {
        var sb = new StringBuilder(token.Length);
        foreach (var c in token)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    /// <summary>Token similarity in [0,1]: exact==1, otherwise a Levenshtein ratio.</summary>
    internal static double Similarity(string a, string b)
    {
        if (a == b)
        {
            return 1.0;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return 0.0;
        }

        var dist = Levenshtein(a, b);
        var max = Math.Max(a.Length, b.Length);
        return 1.0 - (dist / (double)max);
    }

    public static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) { prev[j] = j; }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}
