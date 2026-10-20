using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LyricsLab.Core.Lyrics
{
    public static class TranscriptMerger
    {
        private const double SecondaryConfidenceFloor = 0.85;

        public static IReadOnlyList<TimedWord> MergeTranscripts(
            IReadOnlyList<TimedWord> primary, IReadOnlyList<TimedWord> secondary, Action<string> onLog)
        {
            var result = new List<TimedWord>(primary);
            var added = 0;
            var droppedLowConf = 0;

            foreach (var word in secondary)
            {
                if (word.Confidence < SecondaryConfidenceFloor)
                {
                    droppedLowConf++;
                    continue;
                }

                var norm = NormalizeWord(word.Text);
                var isDup = primary.Any(p =>
                    Math.Abs((p.Start - word.Start).TotalMilliseconds) < 200 &&
                    NormalizeWord(p.Text) == norm);
                
                if (!isDup)
                {
                    result.Add(word);
                    added++;
                }
            }

            onLog($"[merge] Added {added} high-confidence secondary anchor(s); dropped {droppedLowConf} below {SecondaryConfidenceFloor:0.00} confidence.");
            result.Sort((a, b) => a.Start.CompareTo(b.Start));
            return result;
        }

        private static string NormalizeWord(string text) =>
            new string(text.ToLowerInvariant().Where(char.IsLetter).ToArray());
    }
}
