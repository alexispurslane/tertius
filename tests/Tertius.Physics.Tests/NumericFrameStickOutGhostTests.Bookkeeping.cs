using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The places of the ghosts that the numeric update names stay coherent with the frame tree and with physics: a frame that
/// goes takes its places with it, and a place that the update stops naming loses its ghost at the same sync point, so the
/// invariants of the tree and of the backend hold between ticks.
/// </summary>
public partial class NumericFrameStickOutGhostTests
{
    /// <summary>
    /// The scene of <see cref="Layout.StationAndFastRock"/>, in which the numeric update names the rock's falling frame
    /// for the bodies that stick out of the station. Destroying the station's owner outside a tick takes the station's
    /// places with it, so the invariants of the world, which hold the tree's, hold before the next tick.
    /// </summary>
    [Fact]
    public void DestroyingAStationOutsideATickTakesTheFramesThatKeepTheGhostsOfItsBodiesWithIt()
    {
        using World world = NewWorld(numericFrames: true, out _);
        Scene scene = Build(world, Layout.StationAndFastRock);
        Tick(world, 2);
        FrameHandle home = world.PhysicsHomeOf(scene.Rock);
        NamesStickOutGhosts(world, scene.Bubble, home).Should().BeTrue("the rock may touch the crate that sticks out of the station");

        world.Destroy(scene.Owner);

        world.Frames.StickOutGhosts.IsEmpty.Should().BeTrue("the station is gone");
        world.Invoking(w => w.AssertInvariants()).Should().NotThrow();
        world.Tick();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Space is a frame with a bubble of 1,000 km inside a root frame, and the scene of
    /// <see cref="Layout.StationAndFastRock"/>, with a station without a hull, lies in space. Destroying the owner of space outside a tick dissolves the
    /// rock's falling frame, a numeric frame of space, and moves the station to the root frame. So the update's pair of the
    /// station and the rock's frame goes, and the invariants hold before the next tick.
    /// </summary>
    [Fact]
    public void DestroyingTheSemanticParentOfAStationOutsideATickTakesTheDissolvedFramesThatKeptTheGhostsOfItsBodiesWithIt()
    {
        using World world = NewWorld(numericFrames: true, out _);
        FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity spaceOwner = world.Spawn(new SpawnDesc { Frame = root, Pose = At(0), AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static) });
        FrameHandle space = world.GetOwnedFrame(spaceOwner);
        Entity stationOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(40), MotionDriver.Static),
        });
        FrameHandle station = world.GetOwnedFrame(stationOwner);
        world.Spawn(new SpawnDesc { Frame = station, Pose = At(0, 40.5), Body = CrateBody });
        Entity rock = world.Spawn(new SpawnDesc { Frame = space, Pose = At(1100, 41), LinearVelocity = new Double3(-60, 0, 0), Body = RockBody });
        Tick(world, 2);
        FrameHandle home = world.PhysicsHomeOf(rock);
        NamesStickOutGhosts(world, station, home).Should().BeTrue("the rock may touch the crate that sticks out of the station");

        world.Destroy(spaceOwner);

        world.Frames.IsAlive(home).Should().BeFalse("the rock's numeric frame dissolved with space");
        world.Frames.Parent(station).Should().Be(root);
        world.Frames.StickOutGhosts.IsEmpty.Should().BeTrue();
        world.Invoking(w => w.AssertInvariants()).Should().NotThrow();
        world.Tick();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The rock of <see cref="Layout.StationAndFastRock"/> passes 2 m higher, so it misses the crate. The rock flies on past
    /// the station, and the numeric update stops naming the rock's falling frame for the bodies that stick out of the
    /// station, while the frame lives on. The ghost there goes at the same sync point, so the backend keeps its invariants
    /// after every tick, and it stays as long as its place does. The rock meets nothing, with numeric frames or without.
    /// Before ter-dhkf rule 3, the station had the hull of a still Kepler frame, whose ghost the rock's frame kept too, so
    /// the test also saw one place of the frame go while the other stayed; a Kepler hull that holds still is a fixed
    /// anchor now, which the rock's group joins, and the place of a ghost of a Kepler hull has tests of its own in
    /// <see cref="NumericFrameGhostTests"/> and <see cref="GhostContactTests"/>.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGhostWhosePlaceTheNumericUpdateStopsNamingGoesAtTheSameSyncPoint(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        Scene scene = Build(world, Layout.StationAndFastRock, rockOffset: 2);
        world.Tick();
        FrameHandle home = world.PhysicsHomeOf(scene.Rock);
        bool namedStickOut = false, keptStickOutGhost = false, lostStickOutGhost = false;
        bool namedBefore = NamesStickOutGhosts(world, scene.Bubble, home);
        for (int tick = 0; tick < 400; tick++)
        {
            world.Tick();
            faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow($"after tick {world.TickNumber}");
            bool stickOut = NamesStickOutGhosts(world, scene.Bubble, home);
            namedStickOut |= stickOut;
            // Without numeric frames the rock's home is space, which holds the ghost of the crate that sticks out.
            keptStickOutGhost |= numericFrames && !stickOut && faults.Jolt.HasGhostIn(scene.Crate, home);
            // A place that the update still names keeps its ghost.
            lostStickOutGhost |= numericFrames && namedBefore && stickOut && !faults.Jolt.HasGhostIn(scene.Crate, home);
            namedBefore = stickOut;
        }

        world.PhysicsHomeOf(scene.Rock).Should().Be(home, "the rock flies on in its frame");
        namedStickOut.Should().Be(numericFrames, "the rock flies past the crate that sticks out of the station");
        keptStickOutGhost.Should().BeFalse("the ghost of the crate goes when its place goes");
        lostStickOutGhost.Should().BeFalse("the ghost of the crate stays while its place stays");
        world.Frames.StickOutGhosts.IsEmpty.Should().BeTrue("the rock is far past the station");
        scene.RockLog.NewsOf(scene.Crate).Should().BeEmpty("the rock passes above the crate");
        (world.VelocityIn(scene.Rock, scene.Space) - new Double3(-60, 0, 0)).Length.Should().BeLessThan(1e-3, "nothing touched the rock");
        world.Errors.Should().BeEmpty();
    }
}
