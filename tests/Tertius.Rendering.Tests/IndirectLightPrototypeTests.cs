namespace Tertius.Rendering.Tests;

public sealed class IndirectLightPrototypeTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void DisablingOnlyTheNeighborCapKeepsTheSelectedHistory(bool specular, bool singleDiffuseHistory)
    {
        SignalLight light = specular ? SignalLight.Specular : SignalLight.Diffuse;
        var selected = IndirectLightPrototype.Select(enabled: true, disableNeighborCap: true, light: light);

        selected.SingleDiffuseHistory.Should().Be(singleDiffuseHistory);
        selected.LimitIncoming.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TheExistingAllOnPrototypeKeepsItsOriginalHistoryAndCap(bool specular, bool singleDiffuseHistory)
    {
        SignalLight light = specular ? SignalLight.Specular : SignalLight.Diffuse;
        var selected = IndirectLightPrototype.Select(enabled: true, disableNeighborCap: false, light: light);

        selected.SingleDiffuseHistory.Should().Be(singleDiffuseHistory);
        selected.LimitIncoming.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACapOverrideDoesNotEnableTheDisabledPrototype(bool disableNeighborCap)
    {
        var selected = IndirectLightPrototype.Select(
            enabled: false,
            disableNeighborCap: disableNeighborCap,
            light: SignalLight.Diffuse
        );

        selected.SingleDiffuseHistory.Should().BeFalse();
        selected.LimitIncoming.Should().BeFalse();
    }
}
