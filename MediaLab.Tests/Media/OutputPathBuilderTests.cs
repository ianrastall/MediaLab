using VideoLab.Core.Media;
using Lab.UI.Controls;

namespace MediaLab.Tests.Media;

public class OutputPathBuilderTests
{
    [Fact]
    public void ForAudio_AppendsVideoSuffix_NextToAudio()
    {
        var result = OutputPathBuilder.ForAudio(@"C:\music\song.wav");
        Assert.Equal(@"C:\music\song_video.mp4", result);
    }

    [Fact]
    public void ForAudio_HandlesMultipleDotsInStem()
    {
        var result = OutputPathBuilder.ForAudio(@"C:\music\song_24bit_master.flac");
        Assert.Equal(@"C:\music\song_24bit_master_video.mp4", result);
    }

    [Fact]
    public void Partial_InsertsPartialBeforeExtension()
    {
        var result = OutputPathBuilder.Partial(@"C:\out\song_video.mp4");
        Assert.Equal(@"C:\out\song_video.partial.mp4", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForAudio_RejectsEmptyInput(string? input)
    {
        Assert.ThrowsAny<ArgumentException>(() => OutputPathBuilder.ForAudio(input!));
    }
}
