using PetTemplate.Services.Assistant.Transcription;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// Длительность записи по контейнеру (спец. assistant, 6.1) — в тех форматах, что пишет
/// <c>MediaRecorder</c> браузеров (<c>voice-input.component.ts</c>): WebM, MP4, Ogg
/// </summary>
public class AudioDurationUnitTests
{
    /// <summary>Допуск — последний кадр: его длительность метками не записана</summary>
    private const double Precision = 0.1;

    [Theory]
    [InlineData(5)]
    [InlineData(59.5)]
    [InlineData(95)]
    public void WebMIsMeasuredByLastBlock(double seconds)
    {
        Assert.Equal(seconds, AudioDuration.Seconds(AudioContainers.WebM(seconds))!.Value, Precision);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(95)]
    public void FragmentedMp4IsMeasuredByFragments(double seconds)
    {
        Assert.Equal(seconds, AudioDuration.Seconds(AudioContainers.FragmentedMp4(seconds))!.Value, Precision);
    }

    [Fact]
    public void Mp4IsMeasuredByHeader()
    {
        Assert.Equal(42, AudioDuration.Seconds(AudioContainers.Mp4(42))!.Value, Precision);
    }

    [Fact]
    public void OggOpusIsMeasuredByLastGranule()
    {
        Assert.Equal(12.5, AudioDuration.Seconds(AudioContainers.OggOpus(12.5))!.Value, Precision);
    }

    /// <summary>Не контейнер и обрезанный контейнер — длительность неизвестна, а не исключение</summary>
    [Fact]
    public void UnknownOrBrokenIsNull()
    {
        var webm = AudioContainers.WebM(5);

        Assert.Null(AudioDuration.Seconds([1, 2, 3]));
        Assert.Null(AudioDuration.Seconds(webm.AsSpan(0, 30)));
    }
}
