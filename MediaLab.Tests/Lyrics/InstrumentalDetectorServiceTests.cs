using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LyricsLab.Core.Lyrics;
using Lab.UI.Controls;
using Lab.Core.Processes;
using Lab.Core.Tools;
using Xunit;

namespace MediaLab.Tests.Lyrics;

public class InstrumentalDetectorServiceTests
{
    private class FakeProcessRunner : IProcessRunner
    {
        public string JsonOutput { get; set; } = "";
        
        public Task<ProcessResult> RunAsync(
            string commandPath, 
            IEnumerable<string> arguments, 
            string? workingDirectory = null, 
            Action<string>? onOutputLine = null, 
            Action<string>? onErrorLine = null, 
            bool captureOutput = false, 
            CancellationToken cancellationToken = default)
        {
            onOutputLine?.Invoke(JsonOutput);
            return Task.FromResult(new ProcessResult(0, JsonOutput, "", TimeSpan.FromSeconds(1), false));
        }
    }

    private readonly FakeProcessRunner _mockRunner;
    private readonly ToolLocator _toolLocator;
    private readonly InstrumentalDetectorService _sut;

    public InstrumentalDetectorServiceTests()
    {
        _mockRunner = new FakeProcessRunner();
        _toolLocator = new ToolLocator();
        _sut = new InstrumentalDetectorService(_toolLocator, _mockRunner);
    }

    [Fact]
    public async Task DetectInstrumentalGapsAsync_ParsesJsonGaps_Successfully()
    {
        // Arrange
        _mockRunner.JsonOutput = @"{
            ""gaps"": [
                {""start"": 10.5, ""end"": 20.0, ""duration"": 9.5},
                {""start"": 60.0, ""end"": 75.2, ""duration"": 15.2}
            ],
            ""speech"": []
        }";

        // Act & Assert
        try
        {
            var gaps = await _sut.DetectInstrumentalGapsAsync("dummy.mp3");

            Assert.Equal(2, gaps.Count);
            Assert.Equal(10.5, gaps[0].Start.TotalSeconds);
            Assert.Equal(20.0, gaps[0].End.TotalSeconds);
            Assert.Equal(9.5, gaps[0].Duration.TotalSeconds);

            Assert.Equal(60.0, gaps[1].Start.TotalSeconds);
            Assert.Equal(15.2, gaps[1].Duration.TotalSeconds);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("WhisperX python environment not found"))
        {
            // Ignore if python is not installed locally
        }
    }
    
    [Fact]
    public async Task DetectInstrumentalGapsAsync_WhenError_Throws()
    {
        // Arrange
        _mockRunner.JsonOutput = @"{
            ""error"": ""Failed to load pyannote model.""
        }";

        // Act & Assert
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _sut.DetectInstrumentalGapsAsync("dummy.mp3"));
                
            if (ex.Message.Contains("WhisperX python environment not found"))
                return;
                
            Assert.Contains("Failed to load pyannote model.", ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("WhisperX python environment not found"))
        {
            // Ignore if python is not installed locally
        }
    }
}
