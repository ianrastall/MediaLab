using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LyricsLab.Core.Lyrics;
using Xunit;

namespace MediaLab.Tests.Lyrics;

public class OracleTranscriberServiceTests
{
    private static IReadOnlyList<TimedWord> CreateWords(int count)
    {
        var words = new List<TimedWord>();
        for (int i = 0; i < count; i++)
        {
            words.Add(new TimedWord($"word{i}", TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(i + 1)));
        }
        return words;
    }

    [Fact]
    public async Task TranscribeOracleAsync_Throws_WhenAllEnginesFail()
    {
        // Arrange
        var service = new OracleTranscriberService(
            runWhisperX: () => Task.FromException<IReadOnlyList<TimedWord>>(new Exception("WhisperX failed")),
            runWhisper: () => Task.FromException<IReadOnlyList<TimedWord>>(new Exception("Whisper failed")),
            runVosk: () => Task.FromException<IReadOnlyList<TimedWord>>(new Exception("Vosk failed"))
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            service.TranscribeOracleAsync("audio.wav", null, "small", _ => { }));
        
        Assert.Equal("All speech engines failed.", ex.Message);
    }

    [Fact]
    public async Task TranscribeOracleAsync_ReturnsSingle_WhenOnlyOneSucceeds()
    {
        // Arrange
        var expected = CreateWords(10);
        var service = new OracleTranscriberService(
            runWhisperX: () => Task.FromException<IReadOnlyList<TimedWord>>(new Exception("WhisperX failed")),
            runWhisper: () => Task.FromResult(expected),
            runVosk: () => Task.FromException<IReadOnlyList<TimedWord>>(new Exception("Vosk failed"))
        );

        // Act
        var result = await service.TranscribeOracleAsync("audio.wav", null, "small", _ => { });

        // Assert
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task TranscribeOracleAsync_ReturnsPrimary_WhenSecondaryIsTooShort()
    {
        // Arrange
        var primary = CreateWords(100);
        var secondary = CreateWords(10); // Less than 40% of primary
        
        var service = new OracleTranscriberService(
            runWhisperX: () => Task.FromResult(primary),
            runWhisper: () => Task.FromResult(secondary),
            runVosk: null
        );

        // Act
        var result = await service.TranscribeOracleAsync("audio.wav", null, "small", _ => { });

        // Assert
        Assert.Same(primary, result);
    }
}
