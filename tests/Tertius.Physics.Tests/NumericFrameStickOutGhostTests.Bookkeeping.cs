using Tertius.Simulation;

namespace Tertius.Physics.Tests;

/// <summary>
/// The place of a ghost is an input of one numeric update, not state that the tree keeps between updates: it links a body
/// that may touch the ghost only while the body may touch it. Physics keeps the ghost of a body that sticks out of a bubble
/// in the bubble's tree parent alone, so the backend keeps its invariants while a body comes near the ghost and goes away.
/// </summary>
public partial class NumericFrameStickOutGhostTests
{
    /// <summary>
    /// The rock of <see cref="Layout.StationAndFastRock"/> passes 2 m higher, so it misses the crate. When the test starts,
    /// the rock may touch the ghost of the crate, which the station without a hull holds fixed in the system of space, so it
    /// lives there. It flies on past the station, beyond the merge test's reach of the ghost's place, and then nothing
    /// holds it in the system of space, so it falls in a numeric frame of its own. The crate keeps its one ghost in space
    /// throughout, and the backend keeps its invariants after every tick. The rock meets nothing, with numeric frames or
    /// without.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void
        ARockThatPassesACrateThatSticksOutOfAParkedStationLeavesTheSystemOfSpaceWhenItCanNoLongerTouchTheCratesGhost(
            bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        Scene scene = Build(world, Layout.StationAndFastRock, rockOffset: 2);
        world.Tick();
        world.PhysicsHomeOf(scene.Rock).Should().Be(scene.Space,
            "the rock may touch the ghost of the crate, which is fixed in the system of space");
        for (int tick = 0; tick < 400; tick++)
        {
            world.Tick();
            faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should()
                .NotThrow($"after tick {world.TickNumber}");
            faults.Jolt.GhostCount(scene.Crate).Should().Be(1,
                $"after tick {world.TickNumber} the crate has its one ghost, in the station's tree parent");
            faults.Jolt.HasGhostIn(scene.Crate, scene.Space).Should().BeTrue();
        }

        FrameHandle home = world.PhysicsHomeOf(scene.Rock);
        if (numericFrames)
        {
            world.Frames.Numeric(home).Should().Be(NumericMode.Falling,
                "the rock thrusts at 60 m/s, 300 m past the ghost of the crate");
            world.Frames.SemanticOf(home).Should().Be(scene.Space);
        }
        else
        {
            home.Should().Be(scene.Space);
        }

        scene.RockLog.NewsOf(scene.Crate).Should().BeEmpty("the rock passes above the crate");
        // In 431 ticks, 7.2 s, the gentle thrust of 1e-4 m/s² changes the rock's velocity by at most 0.72 mm/s.
        (world.VelocityIn(scene.Rock, scene.Space) - new Double3(-60, 0, 0)).Length.Should()
            .BeLessThan(1e-3, "nothing but its gentle thrust touched the rock");
        world.Errors.Should().BeEmpty();
    }
}
