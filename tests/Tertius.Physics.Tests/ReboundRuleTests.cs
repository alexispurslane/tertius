namespace Tertius.Physics.Tests;

/// <summary>
/// The rule by which a contact keeps its combined restitution: its rebound, the restitution times the approach speed,
/// must be at least 1 m/s. Otherwise the contact does not bounce. A normal velocity below zero is an approach.
/// </summary>
public class ReboundRuleTests
{
    [Fact]
    public void AReboundOfOneMeterPerSecondKeepsItsRestitution() => FrameWorld.KeptRestitution(0.5f, -2f).Should().Be(0.5f);

    [Fact]
    public void AFasterReboundKeepsItsRestitution() => FrameWorld.KeptRestitution(0.8f, -5f).Should().Be(0.8f);

    [Fact]
    public void AReboundSlowerThanOneMeterPerSecondDoesNotBounce() =>
        FrameWorld.KeptRestitution(0.5f, -1.99f).Should().Be(0f, "0.5 · 1.99 m/s is a rebound of 0.995 m/s");

    [Fact]
    public void AnApproachFasterThanOneMeterPerSecondWithASlowReboundDoesNotBounce() =>
        FrameWorld.KeptRestitution(0.1f, -6f).Should().Be(0f, "Jolt would bounce it, at 0.6 m/s");

    [Fact]
    public void BodiesThatMoveApartDoNotBounce() => FrameWorld.KeptRestitution(1f, 3f).Should().Be(0f);
}
