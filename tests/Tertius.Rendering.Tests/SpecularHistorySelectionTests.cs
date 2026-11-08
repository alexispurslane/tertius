namespace Tertius.Rendering.Tests;

/// <summary>
/// The selection of the specular history from TERTIUS_SPECULAR_HISTORY (<see cref="SpecularHistoryMode"/>), once per
/// renderer, before its layouts (ticket tiunr-7haj).
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class SpecularHistorySelectionTests
{
    private const string Switch = "TERTIUS_SPECULAR_HISTORY";

    [Theory]
    [InlineData(null, nameof(SpecularHistoryMode.PreciseUncapped))]
    [InlineData("", nameof(SpecularHistoryMode.PreciseUncapped))]
    [InlineData("precise-uncapped", nameof(SpecularHistoryMode.PreciseUncapped))]
    [InlineData("precise-capped", nameof(SpecularHistoryMode.PreciseCapped))]
    [InlineData("original", nameof(SpecularHistoryMode.Original))]
    public void EachSettingSelectsItsHistory(string? value, string expected)
    {
        SpecularHistoryMode selected = WithSwitch(value, SpecularHistoryMode.Selected);

        selected.Should().Be(Enum.Parse<SpecularHistoryMode>(expected));
    }

    [Fact]
    public void AnUnknownSettingIsRefused()
    {
        Action select = () => WithSwitch("precise", () => SpecularHistoryMode.Selected());

        select.Should().Throw<ArgumentException>().WithMessage("*TERTIUS_SPECULAR_HISTORY*'precise'*");
    }

    private static T WithSwitch<T>(string? value, Func<T> action)
    {
        string? previous = Environment.GetEnvironmentVariable(Switch);
        Environment.SetEnvironmentVariable(Switch, value);
        try
        {
            return action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Switch, previous);
        }
    }
}
