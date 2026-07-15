using System.Globalization;
using PacToolkits.Desktop.Avalonia.Converters;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class FetchStateConverterTests
{
    [Theory]
    [InlineData(AutoFetchState.Ready, "Clock3")]
    [InlineData(AutoFetchState.Running, "Activity")]
    [InlineData(AutoFetchState.Succeeded, "CircleCheck")]
    [InlineData(AutoFetchState.Failed, "CircleX")]
    [InlineData(AutoFetchState.RetryPending, "RotateCcw")]
    [InlineData(AutoFetchState.Retrying, "RotateCcw")]
    [InlineData(AutoFetchState.Paused, "CirclePause")]
    [InlineData(AutoFetchState.Stopped, "Square")]
    public void Icon_matches_fetch_state(AutoFetchState state, string expected)
    {
        var converter = new FetchStateToIconKindConverter();

        var actual = converter.Convert(state, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Retrying_uses_warning_tone()
        => Assert.Equal(StatusTone.Warning, ConverterHelpers.ToneFromFetchState(AutoFetchState.Retrying));
}
