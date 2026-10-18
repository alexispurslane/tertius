using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The contacts of a crate that leaves a bubble or enters one while a rock outside touches it, through the crate's ghost or
/// its real body. A hand-off moves the crate into another system in the step, and only the numeric update of the next sync
/// point links the rock to what it touches there: the crate's real body, or the place of the crate's ghost, which joins
/// the rock to the home of the bubble's hull. So the contact waits one step when the rock's home holds neither, and
/// gameplay hears the same events as without numeric frames.
/// </summary>
public partial class NumericFrameStickOutGhostTests
{
    /// <summary>
    /// The outcome of a run: what the rock heard of the crate, the rock's place and velocity in space, the crate's frame and
    /// the rock's home at the end, and the mode of the rock's home at the moment when the test depends on it.
    /// </summary>
    private sealed record HandOffOutcome(List<ContactNews> RockNews, Double3 RockPosition, Double3 RockVelocity, FrameHandle CrateFrame,
        FrameHandle RockHome, NumericMode RockMode);

    /// <summary>
    /// Space, and <see cref="FarOut"/>, 9,000 km, out in it a parked station: a static hull of 4 m by 4 m by 4 m with a
    /// bubble of radius 40 m, which hangs directly in space. A crate at (0, 40.5, 0) in the station drifts out along +Y at
    /// 2 m/s. A rock rests in space 0.1 m above the crate's top face, outside the bubble. 9,000 km out it alone would rest
    /// in a numeric frame of its own, but it may touch the ghost of the crate, which lives in the home of the station's
    /// static hull, so the merge test links it to the hull, and it stays in the system of space. The ghost of the crate
    /// there reaches the rock after 0.05 s and pushes it at 2 m/s. After about 1.5 s the crate is farther outside the bubble
    /// than its radius plus the exit margin, so it leaves the station into space, where the rock lives. Without numeric
    /// frames, the rock is in the system of space too, and the contact goes on there. The outcome's mode is the mode of the
    /// rock's home after 0.5 s, while the ghost of the crate pushes it.
    /// </summary>
    private static HandOffOutcome RunCrateThatLeavesAStation(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(FarOut),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(40), MotionDriver.Static),
        });
        FrameHandle station = world.GetOwnedFrame(owner);
        var rockLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc { Frame = station, Pose = At(0, 40.5), LinearVelocity = new Double3(0, 2, 0), Body = CrateBody });
        Entity rock = world.Spawn(new SpawnDesc { Frame = space, Pose = At(FarOut, 42.1), Body = RockBody, Components = [rockLog] });
        Tick(world, 30);
        world.GetFrame(crate).Should().Be(station, "the crate leaves the station later");
        rockLog.NewsOf(crate).Should().StartWith(ContactNews.Started, "the ghost of the crate pushes the rock");
        world.PhysicsHomeOf(rock).Should().Be(world.PhysicsHomeOf(owner), "the rock may touch the ghost of the crate, which lives in the home of the station's hull");
        NumericMode rockMode = world.Frames.Numeric(world.PhysicsHomeOf(rock));
        Tick(world, 130);
        world.Errors.Should().BeEmpty();
        return new HandOffOutcome(rockLog.NewsOf(crate), world.PoseIn(rock, space).Position, world.VelocityIn(rock, space), world.GetFrame(crate),
            world.PhysicsHomeOf(rock), rockMode);
    }

    [Fact]
    public void AContactThroughAStickOutGhostGoesOnWhenTheCrateLeavesAParkedStationIntoSpace()
    {
        HandOffOutcome withNumericFrames = RunCrateThatLeavesAStation(numericFrames: true);
        HandOffOutcome withoutNumericFrames = RunCrateThatLeavesAStation(numericFrames: false);

        withoutNumericFrames.CrateFrame.Should().Be(withoutNumericFrames.RockHome, "the crate left the station into space");
        withoutNumericFrames.RockNews.Should().Equal([ContactNews.Started], "the crate pushes the rock without a break");
        withoutNumericFrames.RockVelocity.Y.Should().BeApproximately(2, 0.05, "the crate pushed the rock at its own speed");
        withNumericFrames.RockMode.Should().Be(NumericMode.None, "the ghost of the crate pushes the rock in the system of space, the home of the station's static hull");
        withNumericFrames.CrateFrame.Should().Be(withoutNumericFrames.CrateFrame, "the crate left the station into space");
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews, "numeric frames do not change which events gameplay hears");
        (withNumericFrames.RockPosition - withoutNumericFrames.RockPosition).Length.Should().BeLessThan(1e-2);
        (withNumericFrames.RockVelocity - withoutNumericFrames.RockVelocity).Length.Should().BeLessThan(1e-2);
    }

    /// <summary>
    /// Space, and <see cref="FarOut"/>, 9,000 km, out in it a parked station with no body that can move: a static hull of 4
    /// m by 4 m by 4 m with a bubble of radius 40 m. A kinematic crate drifts at 2 m/s along -X toward the bubble, 20 m
    /// above the hull's path, and a rock that follows it at 2.2 m/s catches up with its back after 0.25 s and then rides on
    /// it. Both are slow and 9,000 km out, so they rest in one numeric frame of space, whose mode is the outcome's mode
    /// after the first tick. After 0.7 s the crate touches the bubble and enters the station, while the rock touches the
    /// bubble only 1.5 s later. Until the hand-off the crate is a body of space, so the last numeric update had no place of
    /// a ghost of it. In the step of the hand-off the crate's ghost goes into the station's tree parent, the system of
    /// space, and the rock's resting frame holds no ghost of it. So the contact waits one step, until the next update links
    /// the rock to the place of the crate's ghost, which joins it to the home of the station's static hull, the system of
    /// space. Without numeric frames, the rock is in the system of space, where the ghost of the crate that sticks out of
    /// the station carries the contact.
    /// </summary>
    private static HandOffOutcome RunCrateThatEntersAnEmptyStation(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(FarOut),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(40), MotionDriver.Static),
        });
        FrameHandle station = world.GetOwnedFrame(owner);
        var rockLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(FarOut + 38, 20),
            LinearVelocity = new Double3(-2, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic),
        });
        Entity rock = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(FarOut + 39.55, 20),
            LinearVelocity = new Double3(-2.2, 0, 0),
            Body = RockBody,
            Components = [rockLog],
        });
        world.Tick();
        if (numericFrames)
            world.PhysicsHomeOf(rock).Should().Be(world.PhysicsHomeOf(crate), "the merge test links the crate and the rock behind it");
        NumericMode rockMode = world.Frames.Numeric(world.PhysicsHomeOf(rock));
        int ticks = TickUntil(world, () => world.GetFrame(crate) == station, 100);
        ticks.Should().BeInRange(30, 50, "the crate touches the station's bubble after about 0.7 s");
        world.GetFrame(rock).Should().Be(space, "the rock touches the bubble later");
        Tick(world, 40);
        world.GetFrame(rock).Should().Be(space, "the rock touches the bubble later");
        world.PhysicsHomeOf(rock).Should().Be(world.PhysicsHomeOf(owner), "the rock may touch the ghost of the crate, which lives in the home of the station's hull");
        world.Errors.Should().BeEmpty();
        return new HandOffOutcome(rockLog.NewsOf(crate), world.PoseIn(rock, space).Position, world.VelocityIn(rock, space), world.GetFrame(crate),
            world.PhysicsHomeOf(rock), rockMode);
    }

    [Fact]
    public void AContactOfACrateThatEntersAStationWithoutBodiesThatMoveGoesOnThroughItsGhost()
    {
        HandOffOutcome withNumericFrames = RunCrateThatEntersAnEmptyStation(numericFrames: true);
        HandOffOutcome withoutNumericFrames = RunCrateThatEntersAnEmptyStation(numericFrames: false);

        withoutNumericFrames.RockNews.Should().Equal([ContactNews.Started], "the rock rides on the crate without a break");
        withoutNumericFrames.RockVelocity.X.Should().BeApproximately(-2, 0.05, "the rock rides on the crate at the crate's speed");
        withNumericFrames.RockMode.Should().Be(NumericMode.Resting, "the crate and the rock are slow and 9,000 km out");
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews, "numeric frames do not change which events gameplay hears");
        (withNumericFrames.RockPosition - withoutNumericFrames.RockPosition).Length.Should().BeLessThan(1e-2);
        (withNumericFrames.RockVelocity - withoutNumericFrames.RockVelocity).Length.Should().BeLessThan(1e-2);
    }

    /// <summary>
    /// Space, and at (150, 0, 0) in it a ship that flies at 60 m/s along -X with a gentle thrust: a hull of 4 m by 4 m by 4 m
    /// and 1000 kg that physics drives, with a bubble of radius 200 m. A crate rests at (0, -199.5, 0) in space, and a rock
    /// rests at (0, -201, 0), against the crate's bottom face, so the two touch from the first step. Both are slow and near
    /// the origin of space, so they rest in the system of space. After the 30 ticks of onset, the hull thrusts, so it falls
    /// in a numeric frame of its own, under which the ship's frame hangs. The hull stays at least 200 m from the crate and
    /// the rock, beyond the merge test's reach of the hull, about 124 m. About 1.5 s later the crate's bounding sphere
    /// touches the ship's bubble, and the crate enters the ship. Its center passes 199.5 m from the ship's center, so it
    /// stays in the ship. The rock's center passes 201 m from the ship's center, 0.5 m more than the bubble's radius plus
    /// the rock's own, so the rock never touches the bubble. In the step of the hand-off the crate's ghost goes into the
    /// ship's tree parent, the hull's falling frame, while the rock lives in the system of space, and neither body is in a
    /// numeric frame. So the contact waits one step, until the next update links the rock to the place of the crate's ghost,
    /// which joins it to the hull's falling frame. Without numeric frames, the ship's tree parent is space, where the ghost
    /// of the crate carries the contact at once (review of ter-4psc). The outcome's mode is the mode of the rock's home 20
    /// ticks after the hand-off. In that home the rock and the ghost of the crate both move at 60 m/s, and Jolt's linear
    /// cast of the rock along the ghost that it touches holds the rock back by 0.58 m along X over the first steps (ticket
    /// ter-91p8). So the test compares the rock's place across X only.
    /// </summary>
    private static HandOffOutcome RunCrateThatEntersAFallingShip(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space, Pose = At(150), LinearVelocity = new Double3(-60, 0, 0), Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(200), MotionDriver.Physics), Components = [new Thruster(ShipHull.Mass)],
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        var rockLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, -199.5), Body = CrateBody });
        Entity rock = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, -201), Body = RockBody, Components = [rockLog] });
        Tick(world, OnsetTicks);
        rockLog.NewsOf(crate).Should().Equal([ContactNews.Started], "the crate and the rock touch from the first step");
        world.PhysicsHomeOf(rock).Should().Be(space, "the rock is slow and near the origin of space");
        if (numericFrames)
            world.Frames.Numeric(world.PhysicsHomeOf(owner)).Should().Be(NumericMode.Falling, "the ship's hull thrusts at 60 m/s");

        int ticks = TickUntil(world, () => world.GetFrame(crate) == ship, 200);

        ticks.Should().BeInRange(80, 100, "the crate touches the ship's bubble about 1.5 s after the onset");
        faults.Jolt.HasGhostIn(crate, world.Frames.TreeParent(ship)).Should().BeTrue("the crate sticks out of the ship into its tree parent");
        world.PhysicsHomeOf(rock).Should().Be(world.Frames.TreeParent(ship),
            "the update of the hand-off's sync point links the rock to the place of the crate's ghost, in the home of the ship's hull");
        if (numericFrames)
            world.Frames.IsNumeric(world.Frames.TreeParent(ship)).Should().BeTrue("the ship's frame hangs in the hull's falling frame");
        Tick(world, 20);
        world.GetFrame(rock).Should().Be(space, "the rock never touches the bubble");
        world.GetFrame(crate).Should().Be(ship, "the crate stays in the ship");
        world.Errors.Should().BeEmpty();
        return new HandOffOutcome(rockLog.NewsOf(crate), world.PoseIn(rock, space).Position, world.VelocityIn(rock, space), world.GetFrame(crate),
            world.PhysicsHomeOf(rock), world.Frames.Numeric(world.PhysicsHomeOf(rock)));
    }

    [Fact]
    public void AContactOfACrateThatEntersAFallingShipWhileItTouchesARockInSpaceGoesOnThroughItsGhost()
    {
        HandOffOutcome withNumericFrames = RunCrateThatEntersAFallingShip(numericFrames: true);
        HandOffOutcome withoutNumericFrames = RunCrateThatEntersAFallingShip(numericFrames: false);

        withoutNumericFrames.RockNews.Should().Equal([ContactNews.Started], "the ghost of the crate in space carries the contact");
        withNumericFrames.RockMode.Should().Be(NumericMode.Falling, "the rock joined the hull's falling frame, where the ghost of the crate lives");
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews, "numeric frames do not change which events gameplay hears");
        withNumericFrames.RockPosition.Y.Should().BeApproximately(withoutNumericFrames.RockPosition.Y, 1e-3);
        withNumericFrames.RockPosition.Z.Should().BeApproximately(withoutNumericFrames.RockPosition.Z, 1e-3);
        (withNumericFrames.RockVelocity - withoutNumericFrames.RockVelocity).Length.Should().BeLessThan(1e-2);
    }
}
