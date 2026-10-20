using System;
using System.Collections.Generic;
using System.Linq;
using LyricsLab.ViewModels;

namespace LyricsLab.Services
{
    public static class AlignmentService
    {
        public static IEnumerable<LyricLineViewModel> GenerateDeterministicAlignment(string rawLyrics, TimeSpan totalDuration)
        {
            if (string.IsNullOrWhiteSpace(rawLyrics) || totalDuration.TotalSeconds <= 0)
                return Enumerable.Empty<LyricLineViewModel>();

            var linesText = new List<string>();

            if (rawLyrics.TrimStart().StartsWith('<'))
            {
                var lyrics = LyricsLab.Core.Lyrics.LyricsXmlParser.Parse(rawLyrics);
                linesText = lyrics.VocalLines().Select(l => l.Data.Text).ToList();
            }
            else
            {
                linesText = SplitRawText(rawLyrics);
            }

            if (linesText.Count == 0)
                return Enumerable.Empty<LyricLineViewModel>();

            var durationPerLine = TimeSpan.FromSeconds(totalDuration.TotalSeconds / linesText.Count);
            var result = new List<LyricLineViewModel>();

            var currentTime = TimeSpan.Zero;
            foreach (var line in linesText)
            {
                var nextTime = currentTime + durationPerLine;
                result.Add(new LyricLineViewModel
                {
                    Text = line,
                    StartTime = currentTime,
                    EndTime = nextTime
                });
                currentTime = nextTime;
            }

            return result;
        }

        private static List<string> SplitRawText(string raw)
        {
            return raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(l => l.Trim())
                      .Where(l => !string.IsNullOrEmpty(l))
                      .ToList();
        }
    }
}
