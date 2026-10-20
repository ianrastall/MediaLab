using System;
using System.Collections.Generic;
using System.Linq;
using LyricsLab.Core.Lyrics;
using LyricsLab.ViewModels;

namespace LyricsLab.Services
{
    public static class AudioAwareAlignmentService
    {
        public static IEnumerable<LyricLineViewModel> GenerateAlignmentFromWhisper(
            string rawLyrics, 
            TimeSpan totalDuration,
            IReadOnlyList<TimedWord> whisperWords)
        {
            if (string.IsNullOrWhiteSpace(rawLyrics) || totalDuration.TotalSeconds <= 0)
                return Enumerable.Empty<LyricLineViewModel>();

            SongLyrics songLyrics;
            try
            {
                songLyrics = LyricsXmlParser.Parse(rawLyrics);
            }
            catch
            {
                // Fallback if not XML
                return AlignmentService.GenerateDeterministicAlignment(rawLyrics, totalDuration);
            }

            var lines = songLyrics.VocalLines().Select(l => l.Data.Text).ToList();
            if (lines.Count == 0) return Enumerable.Empty<LyricLineViewModel>();

            var vocalRegions = ClusterVocalRegions(whisperWords, totalDuration);
            var totalVocalSeconds = vocalRegions.Sum(r => r.Duration.TotalSeconds);
            
            if (totalVocalSeconds <= 0) 
                return AlignmentService.GenerateDeterministicAlignment(rawLyrics, totalDuration);

            var result = new List<LyricLineViewModel>();
            var secondsPerLine = totalVocalSeconds / lines.Count;
            
            int lineIdx = 0;
            foreach (var region in vocalRegions)
            {
                var currentTime = region.Start;
                while (currentTime < region.End && lineIdx < lines.Count)
                {
                    var nextTime = currentTime.Add(TimeSpan.FromSeconds(secondsPerLine));
                    if (nextTime > region.End) nextTime = region.End;

                    result.Add(new LyricLineViewModel
                    {
                        Text = lines[lineIdx],
                        StartTime = currentTime,
                        EndTime = nextTime
                    });
                    
                    currentTime = nextTime;
                    lineIdx++;
                }
            }

            return result;
        }

        private static List<(TimeSpan Start, TimeSpan End, TimeSpan Duration)> ClusterVocalRegions(IReadOnlyList<TimedWord> words, TimeSpan totalDuration)
        {
            var regions = new List<(TimeSpan Start, TimeSpan End, TimeSpan Duration)>();
            if (words.Count == 0) return regions;

            var sorted = words.OrderBy(w => w.Start).ToList();
            var currentStart = sorted[0].Start;
            var currentEnd = sorted[0].End;
            var maxGap = TimeSpan.FromSeconds(2.5); // Allow 2.5 seconds of gap between words in a single phrase

            foreach (var word in sorted.Skip(1))
            {
                if (word.Start - currentEnd <= maxGap)
                {
                    // Extend current region
                    currentEnd = word.End > currentEnd ? word.End : currentEnd;
                }
                else
                {
                    // Gap is too large, finalize previous region
                    regions.Add((currentStart, currentEnd, currentEnd - currentStart));
                    currentStart = word.Start;
                    currentEnd = word.End;
                }
            }
            
            regions.Add((currentStart, currentEnd, currentEnd - currentStart));
            return regions;
        }
    }
}
