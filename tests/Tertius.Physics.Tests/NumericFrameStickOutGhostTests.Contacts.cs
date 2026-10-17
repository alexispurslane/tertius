using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The contacts of a body that leaves a bubble or enters one, while a ghost in a numeric frame holds the contact or would
/// hold it. A hand-off moves the body into another system in the step, and only the numeric update of the next sync point
/// links it to what it touches there, or names the frame that keeps its ghost. So the contact waits one step, and gameplay
/// hears the same events as without numeric frames.
/// </summary>
public partial class NumericFrameStickOutGhostTests
{
    /// <summary>The outcome of a run: what the rock heard of the crate, the rock's place and velocity in space, and the crate's frame at the end.</summary>
    private sealed record HandOffOutcome(List<ContactNews> RockNews, Double3 RockPosition, Double3 RockVelocity, FrameHandle CrateFrame,
        FrameHandle RockHome);

    /// <summary>
    /// Space, and 20 km out in it a parked station: a static hull of 4 m by 4 m by 4 m with a bubble of radius 40 m, which
    /// hangs directly in space. A crate at (0, 40.5, 0) in the station drifts out along +Y at 2 m/s. A rock rests in space
    /// 0.1 m above the crate's top face, outside the bubble, so it rests 20 km out in a numeric frame of its own, and the
    /// numeric update names that frame for the bodies that stick out of the station. The ghost of the crate there reaches
    /// the rock after 0.05 s and pushes it at 2 m/s. After about 1.5 s the crate is farther outside the bubble than its
    /// radius plus the exit margin, so it leaves the station into space, which is not numeric. Without numeric frames, the
    /// rock is in the system of space, and the contact goes on there.
    /// </summary>
    private static HandOffOutcome RunCrateThatLeavesAStation(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(20_000),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(40), MotionDriver.Static),
        });
        FrameHandle station = world.GetOwnedFrame(owner);
        var rockLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc { Frame = station, Pose = At(0, 40.5), LinearVelocity = new Double3(0, 2, 0), Body = CrateBody });
        Entity rock = world.Spawn(new SpawnDesc { Frame = space, Pose = At(20_000, 42.1), Body = RockBody, Components = [rockLog] });
        Tick(world, 160);
        world.Errors.Should().BeEmpty();
        return new HandOffOutcome(rockLog.NewsOf(crate), world.PoseIn(rock, space).Position, world.VelocityIn(rock, space), world.GetFrame(crate),
            world.PhysicsHomeOf(rock));
    }

    [Fact]
    public void AContactThroughAStickOutGhostGoesOnWhenTheCrateLeavesAParkedStationIntoSpace()
    {
        HandOffOutcome withNumericFrames = RunCrateThatLeavesAStation(numericFrames: true);
        HandOffOutcome withoutNumericFrames = RunCrateThatLeavesAStation(numericFrames: false);

        withoutNumericFrames.CrateFrame.Should().Be(withoutNumericFrames.RockHome, "the crate left the station into space");
        withoutNumericFrames.RockNews.Should().Equal([ContactNews.Started], "the crate pushes the rock without a break");
        withoutNumericFrames.RockVelocity.Y.Should().BeApproximately(2, 0.05, "the crate pushed the rock at its own speed");
        withNumericFrames.CrateFrame.Should().Be(withoutNumericFrames.CrateFrame, "the crate left the station into space");
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews, "numeric frames do not change which events gameplay hears");
        (withNumericFrames.RockPosition - withoutNumericFrames.RockPosition).Length.Should().BeLessThan(1e-2);
        (withNumericFrames.RockVelocity - withoutNumericFrames.RockVelocity).Length.Should().BeLessThan(1e-2);
    }

    /// <summary>
    /// Space, and 20 km out in it a parked station with no body that can move: a static hull of 4 m by 4 m by 4 m with a
    /// bubble of radius 40 m. A kinematic crate drifts at 2 m/s along -X toward the bubble, 20 m above the hull's path, and
    /// a rock that follows it at 2.2 m/s catches up with its back after 0.25 s and then rides on it. Both are slow and 20
    /// km out, so they rest in one numeric frame of space. After 0.7 s the crate touches the bubble and enters the station,
    /// while the rock touches the bubble only 1.5 s later. The last numeric update listed no body of the station that can
    /// move, so it named no frame for the bodies that stick out of it, and in the step of the hand-off no ghost of the
    /// crate is in the rock's frame. Without numeric frames, the rock is in the system of space, where the ghost of the
    /// crate that sticks out of the station carries the contact.
    /// </summary>
    private static HandOffOutcome RunCrateThatEntersAnEmptyStation(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(20_000),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(40), MotionDriver.Static),
        });
        FrameHandle station = world.GetOwnedFrame(owner);
        var rockLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(20_038, 20),
            LinearVelocity = new Double3(-2, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic),
        });
        Entity rock = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(20_039.55, 20),
            LinearVelocity = new Double3(-2.2, 0, 0),
            Body = RockBody,
            Components = [rockLog],
        });
        world.Tick();
        if (numericFrames)
            world.PhysicsHomeOf(rock).Should().Be(world.PhysicsHomeOf(crate), "the merge test links the crate and the rock behind it");
        int ticks = TickUntil(world, () => world.GetFrame(crate) == station, 100);
        ticks.Should().BeInRange(30, 50, "the crate touches the station's bubble after about 0.7 s");
        world.GetFrame(rock).Should().Be(space, "the rock touches the bubble later");
        Tick(world, 40);
        world.GetFrame(rock).Should().Be(space, "the rock touches the bubble later");
        world.Errors.Should().BeEmpty();
        return new HandOffOutcome(rockLog.NewsOf(crate), world.PoseIn(rock, space).Position, world.VelocityIn(rock, space), world.GetFrame(crate),
            world.PhysicsHomeOf(rock));
    }

    [Fact]
    public void AContactOfACrateThatEntersAStationWithoutBodiesThatMoveGoesOnThroughItsGhost()
    {
        HandOffOutcome withNumericFrames = RunCrateThatEntersAnEmptyStation(numericFrames: true);
        HandOffOutcome withoutNumericFrames = RunCrateThatEntersAnEmptyStation(numericFrames: false);

        withoutNumericFrames.RockNews.Should().Equal([ContactNews.Started], "the rock rides on the crate without a break");
        withoutNumericFrames.RockVelocity.X.Should().BeApproximately(-2, 0.05, "the rock rides on the crate at the crate's speed");
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews, "numeric frames do not change which events gameplay hears");
        (withNumericFrames.RockPosition - withoutNumericFrames.RockPosition).Length.Should().BeLessThan(1e-2);
        (withNumericFrames.RockVelocity - withoutNumericFrames.RockVelocity).Length.Should().BeLessThan(1e-2);
    }
}
