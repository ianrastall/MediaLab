using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LyricsLab.Core.Lyrics;

namespace LyricsLab.UI.ViewModels;

/// <summary>A source lyric line that hasn't yet been assigned to a section.</summary>
public sealed class SourceLine
{
    public int OriginalIndex { get; init; }
    public required string Text { get; init; }
    public override string ToString() => Text;
}

/// <summary>A named section type shown as an assignment button in the editor.</summary>
public sealed record LyricSectionType(string Code, string Label);

/// <summary>One structured section being built in the editor.</summary>
public sealed partial class EditorSection : ObservableObject
{
    [ObservableProperty] private string type = "verse";
    [ObservableProperty] private string durationText = "";

    public IReadOnlyList<SourceLine> Lines { get; init; } = [];

    public double SortIndex { get; set; }

    public bool IsInstrumental =>
        string.Equals(Type, "instrumental", StringComparison.OrdinalIgnoreCase);

    public string Summary => IsInstrumental
        ? $"instrumental{(string.IsNullOrWhiteSpace(DurationText) ? "" : $" · ~{DurationText} s")}"
        : $"{Type}  ·  {Lines.Count} line{(Lines.Count == 1 ? "" : "s")}";

    public string? FirstLinePreview => IsInstrumental ? null : Lines.FirstOrDefault()?.Text;
}

/// <summary>
/// View-model for the lyrics structure editor.  The user selects lines from the left pane and
/// "claims" them into a named section; sections assemble in order on the right; the source pane
/// drains to zero as every line is assigned.
/// </summary>
public sealed partial class LyricsEditorViewModel : ObservableObject
{
    public ObservableCollection<SourceLine> RemainingLines { get; } = [];
    public ObservableCollection<EditorSection> Sections { get; } = [];

    [ObservableProperty] private int remainingCount;
    [ObservableProperty] private bool hasRemainingLines;
    [ObservableProperty] private string newLineText = "";
    [ObservableProperty] private string statusText = "Ready.";

    private IList _selection = Array.Empty<SourceLine>();

    // ── Available section types ──────────────────────────────────────────────

    public static IReadOnlyList<LyricSectionType> VocalTypes { get; } =
    [
        new("intro",      "Intro"),
        new("verse",      "Verse"),
        new("pre-chorus", "Pre-Chorus"),
        new("chorus",     "Chorus"),
        new("bridge",     "Bridge"),
        new("outro",      "Outro"),
        new("spoken",     "Spoken"),
        new("hook",       "Hook"),
    ];

    // ── Initialise from existing lyrics text ────────────────────────────────

    /// <summary>
    /// Populates the editor.  If <paramref name="text"/> is valid XML the existing sections are
    /// shown on the right (source is empty — all lines already claimed); otherwise every non-blank
    /// line appears in the source pane ready to be assigned.
    /// </summary>
    public void LoadFrom(string text)
    {
        RemainingLines.Clear();
        Sections.Clear();

        if (text.TrimStart().StartsWith('<'))
        {
            var lyrics = LyricsXmlParser.Parse(text);
            var idx = 0;
            foreach (var section in lyrics.Sections)
            {
                var sourceLines = section.Lines
                    .Select(l => new SourceLine { OriginalIndex = idx++, Text = l.Text })
                    .ToList();

                Sections.Add(new EditorSection
                {
                    Type = section.Type,
                    DurationText = section.DurationHint.HasValue
                        ? section.DurationHint.Value.ToString(CultureInfo.InvariantCulture)
                        : "",
                    Lines = sourceLines,
                    SortIndex = sourceLines.Count > 0 ? sourceLines[0].OriginalIndex : idx++
                });
            }
            RemainingCount = 0;
            HasRemainingLines = false;
            return;
        }

        // Plain text — one SourceLine per non-blank line.
        var i = 0;
        foreach (var raw in text.Split('\n'))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0)
                RemainingLines.Add(new SourceLine { OriginalIndex = i++, Text = trimmed });
        }
        RemainingCount = RemainingLines.Count;
        HasRemainingLines = RemainingLines.Count > 0;
    }

    // ── Dependencies for Auto-Detect ────────────────────────────────────────

    private string? _audioFilePath;
    private OracleTranscriberService? _oracle;
    private LyricsLab.Core.Lyrics.InstrumentalDetectorService? _vadDetector;
    private Action<string>? _onLog;

    public void InitDependencies(
        string? audioFilePath, 
        OracleTranscriberService? oracle, 
        LyricsLab.Core.Lyrics.InstrumentalDetectorService? vadDetector,
        Action<string>? onLog)
    {
        _audioFilePath = audioFilePath;
        _oracle = oracle;
        _vadDetector = vadDetector;
        _onLog = onLog;
        AutoDetectInstrumentalsCommand.NotifyCanExecuteChanged();
    }

    // ── Selection bridge (set from code-behind) ─────────────────────────────

    public void SetSelection(IList selected)
    {
        _selection = selected ?? Array.Empty<SourceLine>();
        ClaimAsCommand.NotifyCanExecuteChanged();
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    private bool CanAutoDetectInstrumentals() => 
        !string.IsNullOrWhiteSpace(_audioFilePath) && _oracle != null && _vadDetector != null && Sections.Count > 0;

    [RelayCommand(CanExecute = nameof(CanAutoDetectInstrumentals))]
    private async Task AutoDetectInstrumentalsAsync()
    {
        if (!CanAutoDetectInstrumentals()) return;
        
        try
        {
            _onLog?.Invoke("Running VAD...");
            var gaps = await _vadDetector!.DetectInstrumentalGapsAsync(_audioFilePath!, _onLog);
            if (gaps.Count == 0)
            {
                _onLog?.Invoke("No instrumental gaps > 5s detected.");
                return;
            }

            _onLog?.Invoke("Running quick alignment to place instrumentals...");
            
            var tempXml = GenerateXml();
            var song = LyricsXmlParser.Parse(tempXml);
            
            var transcript = await _oracle!.TranscribeOracleAsync(_audioFilePath!, tempXml, "small", _onLog ?? (_ => { }));
            
            var options = new AlignerOptions { TotalDuration = null };
            var alignedLines = LyricsAligner.Align(song, transcript, options);
            
            var sectionBounds = new Dictionary<int, (TimeSpan Start, TimeSpan End)>();
            
            int alignedIdx = 0;
            for (int s = 0; s < song.Sections.Count; s++)
            {
                var sec = song.Sections[s];
                if (sec.IsInstrumental || sec.Lines.Count == 0 || sec.IsReference) continue;
                
                if (alignedIdx >= alignedLines.Count) break;

                var firstLine = alignedLines[alignedIdx];
                var lastLine = alignedLines[Math.Min(alignedLines.Count - 1, alignedIdx + sec.Lines.Count - 1)];
                
                sectionBounds[s] = (firstLine.Start, lastLine.End);
                alignedIdx += sec.Lines.Count;
            }
            
            var sectionsToInsert = new List<(int AfterIndex, EditorSection Sec)>();
            
            foreach (var gap in gaps)
            {
                int bestAfter = -1;
                for (int s = 0; s < song.Sections.Count; s++)
                {
                    if (!sectionBounds.ContainsKey(s)) continue;
                    
                    if (sectionBounds[s].End <= gap.Start + TimeSpan.FromSeconds(3))
                    {
                        bestAfter = s;
                    }
                }
                
                double sortIndex = 0;
                if (bestAfter == -1)
                {
                    if (Sections.Count > 0) sortIndex = Sections[0].SortIndex - 1.0;
                }
                else
                {
                    var prevSec = Sections[bestAfter];
                    double nextIdx = bestAfter + 1 < Sections.Count ? Sections[bestAfter + 1].SortIndex : prevSec.SortIndex + 1.0;
                    sortIndex = prevSec.SortIndex + (nextIdx - prevSec.SortIndex) / 2.0;
                }
                
                sectionsToInsert.Add((bestAfter, new EditorSection
                {
                    Type = "instrumental",
                    DurationText = gap.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture),
                    Lines = [],
                    SortIndex = sortIndex
                }));
            }
            
            int inserted = 0;
            foreach (var insertion in sectionsToInsert.OrderByDescending(x => x.AfterIndex))
            {
                int insertAt = insertion.AfterIndex + 1;
                
                bool alreadyHasInstrumental = false;
                if (insertAt < Sections.Count && Sections[insertAt].IsInstrumental) alreadyHasInstrumental = true;
                if (insertAt > 0 && Sections[insertAt - 1].IsInstrumental) alreadyHasInstrumental = true;
                    
                if (!alreadyHasInstrumental)
                {
                    Sections.Insert(insertAt, insertion.Sec);
                    inserted++;
                }
            }
            
            _onLog?.Invoke($"Inserted {inserted} instrumental sections.");
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Auto-Detect Error: {ex.Message}");
        }
    }

    partial void OnNewLineTextChanged(string value) =>
        AddLineCommand.NotifyCanExecuteChanged();

    private bool CanAddLine() => !string.IsNullOrWhiteSpace(NewLineText);

    /// <summary>
    /// Adds a manually typed line to the source list so it can be claimed into a section.
    /// Useful for speech samples or outros not included in the original lyrics file.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddLine))]
    private void AddLine()
    {
        var text = NewLineText.Trim();
        if (text.Length == 0) return;

        // Pick an index higher than everything else so re-insertion order stays sane.
        var maxIdx = RemainingLines.Count > 0 ? RemainingLines.Max(l => l.OriginalIndex) : -1;
        foreach (var section in Sections)
            foreach (var line in section.Lines)
                maxIdx = Math.Max(maxIdx, line.OriginalIndex);

        RemainingLines.Add(new SourceLine { OriginalIndex = maxIdx + 1, Text = text });
        RemainingCount = RemainingLines.Count;
        HasRemainingLines = true;
        NewLineText = "";
    }

    private bool CanClaimAs(string? _) => _selection.Count > 0;

    /// <summary>Claim the selected source lines as a new section of the given type.</summary>
    [RelayCommand(CanExecute = nameof(CanClaimAs))]
    private void ClaimAs(string sectionType)
    {
        if (_selection.Count == 0) return;

        var ordered = _selection.OfType<SourceLine>()
            .OrderBy(l => l.OriginalIndex)
            .ToList();

        var targetNorms = ordered.Select(l => NormalizeForMatch(l.Text)).ToList();

        ClaimSection(sectionType, ordered);

        // Auto-match repeating sections in the remaining lines
        bool foundMatch;
        do
        {
            foundMatch = false;
            for (int i = 0; i <= RemainingLines.Count - targetNorms.Count; i++)
            {
                bool match = true;
                for (int j = 0; j < targetNorms.Count; j++)
                {
                    if (NormalizeForMatch(RemainingLines[i + j].Text) != targetNorms[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    var matchedLines = RemainingLines.Skip(i).Take(targetNorms.Count).ToList();
                    ClaimSection(sectionType, matchedLines);
                    foundMatch = true;
                    break;
                }
            }
        } while (foundMatch);

        RemainingCount = RemainingLines.Count;
        HasRemainingLines = RemainingLines.Count > 0;
    }

    private void ClaimSection(string sectionType, List<SourceLine> lines)
    {
        foreach (var line in lines)
            RemainingLines.Remove(line);

        var newSection = new EditorSection 
        { 
            Type = sectionType, 
            Lines = lines,
            SortIndex = lines.Count > 0 ? lines[0].OriginalIndex : 0
        };

        int insertIdx = 0;
        while (insertIdx < Sections.Count && Sections[insertIdx].SortIndex <= newSection.SortIndex)
        {
            insertIdx++;
        }
        Sections.Insert(insertIdx, newSection);
    }

    private static string NormalizeForMatch(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Trim();
    }


    /// <summary>Insert an instrumental break after the last section (no lines consumed).</summary>
    [RelayCommand]
    private void AddInstrumental()
    {
        double maxIdx = -1;
        if (Sections.Count > 0)
            maxIdx = Sections.Max(s => s.SortIndex);

        Sections.Add(new EditorSection { Type = "instrumental", Lines = [], SortIndex = maxIdx + 0.5 });
    }

    [RelayCommand]
    private void InsertInstrumentalAfter(EditorSection section)
    {
        var i = Sections.IndexOf(section);
        if (i < 0) return;
        
        Sections.Insert(i + 1, new EditorSection 
        { 
            Type = "instrumental", 
            Lines = [], 
            SortIndex = section.SortIndex + 0.1 
        });
    }

    [RelayCommand]
    private void DuplicateSection(EditorSection section)
    {
        var i = Sections.IndexOf(section);
        if (i < 0) return;
        
        var clonedLines = section.Lines
            .Select(l => new SourceLine { OriginalIndex = l.OriginalIndex, Text = l.Text })
            .ToList();

        // Attempt to find and consume a matching block in the RemainingLines
        if (clonedLines.Count > 0 && RemainingLines.Count >= clonedLines.Count)
        {
            var bestIdx = -1;
            var bestScore = int.MaxValue;
            var targetNorms = clonedLines.Select(l => NormalizeForMatch(l.Text)).ToList();

            for (int r = 0; r <= RemainingLines.Count - clonedLines.Count; r++)
            {
                var candidateNorms = RemainingLines.Skip(r).Take(clonedLines.Count)
                                         .Select(l => NormalizeForMatch(l.Text)).ToList();
                
                int blockScore = 0;
                bool allMatched = true;

                // For each line in the candidate block, find the best match in the target
                // This allows matching even if lines are swapped (like Creep's 2nd pre-chorus)
                var usedTargets = new HashSet<int>();
                foreach (var cNorm in candidateNorms)
                {
                    int bestLineScore = int.MaxValue;
                    int bestLineIdx = -1;
                    
                    for (int t = 0; t < targetNorms.Count; t++)
                    {
                        if (usedTargets.Contains(t)) continue;
                        
                        int dist = LyricsAligner.Levenshtein(cNorm, targetNorms[t]);
                        if (dist < bestLineScore)
                        {
                            bestLineScore = dist;
                            bestLineIdx = t;
                        }
                    }
                    
                    if (bestLineIdx >= 0 && bestLineScore <= 5)
                    {
                        usedTargets.Add(bestLineIdx);
                        blockScore += bestLineScore;
                    }
                    else
                    {
                        allMatched = false;
                        break;
                    }
                }

                if (allMatched && blockScore < bestScore)
                {
                    bestScore = blockScore;
                    bestIdx = r;
                }
            }

            // If we found a block where all lines matched reasonably well, consume it
            if (bestIdx >= 0)
            {
                var consumed = RemainingLines.Skip(bestIdx).Take(clonedLines.Count).ToList();
                foreach (var c in consumed)
                    RemainingLines.Remove(c);
                    
                RemainingCount = RemainingLines.Count;
                HasRemainingLines = RemainingLines.Count > 0;
            }
        }
            
        Sections.Insert(i + 1, new EditorSection 
        { 
            Type = section.Type, 
            DurationText = section.DurationText,
            Lines = clonedLines, 
            SortIndex = section.SortIndex + 0.1 
        });
    }

    /// <summary>Remove a section, returning its lines to the source pane in original order.</summary>
    [RelayCommand]
    private void RemoveSection(EditorSection section)
    {
        foreach (var line in section.Lines)
        {
            var insertAt = RemainingLines.TakeWhile(r => r.OriginalIndex < line.OriginalIndex).Count();
            RemainingLines.Insert(insertAt, line);
        }
        Sections.Remove(section);
        RemainingCount = RemainingLines.Count;
        HasRemainingLines = RemainingLines.Count > 0;
    }

    [RelayCommand]
    private void MoveUp(EditorSection section)
    {
        var i = Sections.IndexOf(section);
        if (i > 0)
        {
            var prev = Sections[i - 1];
            (section.SortIndex, prev.SortIndex) = (prev.SortIndex, section.SortIndex);
            Sections.Move(i, i - 1);
        }
    }

    [RelayCommand]
    private void MoveDown(EditorSection section)
    {
        var i = Sections.IndexOf(section);
        if (i < Sections.Count - 1)
        {
            var next = Sections[i + 1];
            (section.SortIndex, next.SortIndex) = (next.SortIndex, section.SortIndex);
            Sections.Move(i, i + 1);
        }
    }

    // ── XML generation ───────────────────────────────────────────────────────

    public string GenerateXml()
    {
        var root = new XElement("song");
        foreach (var section in Sections)
        {
            var el = new XElement("section");
            el.SetAttributeValue("type", section.Type);

            if (section.IsInstrumental)
            {
                if (!string.IsNullOrWhiteSpace(section.DurationText)
                    && double.TryParse(section.DurationText, NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out var dur))
                    el.SetAttributeValue("duration_hint",
                        dur.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                foreach (var line in section.Lines)
                    el.Add(new XElement("line", line.Text));
            }

            root.Add(el);
        }
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }
}
