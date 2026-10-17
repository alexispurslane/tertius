using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A body whose physics home is a numeric frame has a ghost in the bubble of each semantic frame that it touches without
/// entering, as it has without numeric frames (ticket ter-ip1k). A ghost is a kinematic copy of a body in the physics
/// system of another frame, so that the bodies there collide with it. A numeric frame has no bubble, so it does not change
/// which bubbles a body meets. A frame's hull enters another frame only if it fits there: its bubble is less than half as
/// wide as the other one. A hull that does not fit has a ghost in the other frame while the two bubbles overlap. The same
/// rule holds for the hull of a frame that physics does not drive, which is an anchor, and for a body that sticks out of its
/// frame's bubble, which has ghosts in the bubbles of its frame's semantic siblings that it touches. Such a body also has
/// a ghost in its frame's semantic parent when the frame's tree parent is a numeric frame (ticket ter-wllb). These
/// tests run the World on the real backend, with the default numeric frame options, under which a body that flies at
/// 50 m/s or more relative to its frame gets a falling numeric frame, and a slow body 10 km or more from its frame's
/// origin gets a resting one, and with no numeric frames, where the hulls stay in the system of space.
/// </summary>
public class NumericFrameGhostTests
{
    private const float Dt = 1f / 60f;

    /// <summary>The radius of the station's bubble, in meters.</summary>
    private const float StationRadius = 40;

    /// <summary>The radius of the fast ship's bubble, in meters. It is more than half the station's, so the ship does not fit there.</summary>
    private const float ShipRadius = 30;

    /// <summary>How fast the ship flies along -X in space, in m/s.</summary>
    private const float ShipSpeed = 60;

    private static readonly Double3 StationCenter = new(1000, 0, 0);

    /// <summary>Membership rules under which no group of bodies ever gets a numeric frame.</summary>
    private static readonly NumericFrameOptions WithoutNumericFrames = new()
    {
        CreateSpeed = double.PositiveInfinity,
        LeaveSpeed = double.PositiveInfinity,
        CreateDistance = double.PositiveInfinity,
        LeaveDistance = double.PositiveInfinity,
    };

    /// <summary>A world on the wrapper with the default numeric frames or, when <paramref name="numericFrames"/> is false, with none.</summary>
    private static World NewWorld(bool numericFrames, out FaultInjectingBackend faults) =>
        NewWorldWithFaults(out faults, configure: numericFrames ? null : options => options.NumericFrames = WithoutNumericFrames);

    /// <summary>A hull of 4 m by 4 m by 4 m and 1000 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc ShipHull = new(new BoxShapeDesc(new Vector3(2)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A crate of 2 m by 2 m by 2 m and 2 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc CrateBody = new(new BoxShapeDesc(new Vector3(1)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>The station scene.</summary>
    /// <param name="Crate">The crate in the station, or null when the scene has none.</param>
    /// <param name="ShipCrate">The crate in the ship, or null when the scene has none.</param>
    private sealed record StationScene(FrameHandle Space, Entity StationOwner, FrameHandle Station, Entity Crate, Entity ShipOwner, FrameHandle Ship, Entity ShipCrate);

    /// <summary>
    /// Space, and 1 km along +X in it a parked station: a frame that physics does not drive, with a bubble of radius 40 m
    /// around a static hull of 4 m by 4 m by 4 m at its center. The hull is an anchor in the system of space. A crate of
    /// 2 m by 2 m by 2 m and 2 kg floats in the station at (15, 10, 0). A ship flies past at 60 m/s along -X from
    /// (1100, <paramref name="lane"/>, 0) in space: a hull that physics drives, with a bubble of radius 30 m, which does
    /// not fit in the station's.
    /// </summary>
    private static StationScene ShipPassingAStation(World world, double lane) => CratesPassingAStation(world, lane, At(15, 10), shipCrate: null);

    /// <summary>
    /// The station and the ship of <see cref="ShipPassingAStation"/>, with a crate of 2 m by 2 m by 2 m and 2 kg at rest at
    /// <paramref name="stationCrate"/> in the station and another at <paramref name="shipCrate"/> in the ship, or no crate
    /// where the place is null. The ship starts at (<paramref name="shipX"/>, <paramref name="lane"/>, 0) in space, and space
    /// has the field <paramref name="gravity"/>, or none.
    /// </summary>
    private static StationScene CratesPassingAStation(World world, double lane, DPose? stationCrate, DPose? shipCrate,
        double shipX = 1100, GravityField gravity = default)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static, gravity));
        Entity stationOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(StationCenter, DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(StationRadius), MotionDriver.Static),
        });
        FrameHandle station = world.GetOwnedFrame(stationOwner);
        Entity crate = stationCrate is { } inStation ? world.Spawn(new SpawnDesc { Frame = station, Pose = inStation, Body = CrateBody }) : Entity.Null;
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(shipX, lane),
            LinearVelocity = new Vector3(-ShipSpeed, 0, 0),
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(ShipRadius), MotionDriver.Physics),
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        Entity cargo = shipCrate is { } inShip ? world.Spawn(new SpawnDesc { Frame = ship, Pose = inShip, Body = CrateBody }) : Entity.Null;
        return new StationScene(space, stationOwner, station, crate, shipOwner, ship, cargo);
    }

    /// <summary>The distance in meters from the station's center to the ship's, at the end of the last step.</summary>
    private static double ShipDistance(World world, StationScene scene) =>
        (world.PoseIn(scene.ShipOwner, scene.Space).Position - StationCenter).Length;

    /// <summary>Checks that the ship's hull is in a falling numeric frame with numeric frames, and in the system of space without.</summary>
    private static void ShipHomeShouldBe(World world, StationScene scene, bool numericFrames)
    {
        FrameHandle home = world.PhysicsHomeOf(scene.ShipOwner);
        if (numericFrames)
        {
            world.Frames.Numeric(home).Should().Be(NumericMode.Falling, "at 60 m/s the ship's hull gets a falling numeric frame");
            world.Frames.TreeParent(home).Should().Be(scene.Space);
        }
        else
        {
            home.Should().Be(scene.Space);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AShipThatDoesNotFitInAParkedStationHasAGhostThereExactlyWhileTheirBubblesOverlap(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        StationScene scene = ShipPassingAStation(world, lane: 10);
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames);

        // The bubbles overlap while the centers are at most 70 m apart: from about 0.5 s to about 2.8 s.
        int overlapping = 0;
        for (int tick = 0; tick < 200; tick++)
        {
            world.Tick();
            double distance = ShipDistance(world, scene);
            bool overlap = distance <= StationRadius + ShipRadius;
            faults.Jolt.HasGhostIn(scene.ShipOwner, scene.Station).Should().Be(overlap,
                $"after tick {tick} the ship's center is {distance:F3} m from the station's, and a hull reaches as far as its own bubble");
            if (overlap)
                overlapping++;
        }

        overlapping.Should().BeInRange(130, 145, "the bubbles overlap along 138 m of the ship's path");
        world.GetFrame(scene.ShipOwner).Should().Be(scene.Space, "the ship does not fit in the station, so it never enters it");
        faults.Outputs.SelectMany(output => output.HandOffs).Should().NotContain(h => h.Entity == scene.ShipOwner && h.To == scene.Station);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Runs the station scene with the ship in the crate's lane until the crate in the station moves at more than 10 m/s,
    /// at most 150 ticks. Returns the ticks that it ran and the crate's state then, relative to the station.
    /// </summary>
    private static (int Ticks, Double3 Position, Double3 Velocity, Double3 ShipPosition) PushTheCrate(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        StationScene scene = ShipPassingAStation(world, lane: 10);
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames);

        int ticks = TickUntil(world, () => world.VelocityIn(scene.Crate, scene.Station).Length > 10, 150);

        world.Errors.Should().BeEmpty();
        return (ticks, world.PoseIn(scene.Crate, scene.Station).Position, world.VelocityIn(scene.Crate, scene.Station),
            world.PoseIn(scene.ShipOwner, scene.Station).Position);
    }

    [Fact]
    public void TheGhostOfAShipInANumericFramePushesACrateInTheStationAsItDoesWithoutNumericFrames()
    {
        var withNumericFrames = PushTheCrate(numericFrames: true);
        var withoutNumericFrames = PushTheCrate(numericFrames: false);

        // The front of the hull, 2 m ahead of its center, reaches the back of the crate, at x = 16 in the station, after
        // about 82 m, or 1.37 s.
        withoutNumericFrames.Ticks.Should().BeInRange(80, 85, "the ghost of the hull reaches the crate after about 1.37 s");
        withoutNumericFrames.Velocity.X.Should().BeLessThan(-10, "the ghost pushed the crate along -X");
        (withoutNumericFrames.ShipPosition.X - withoutNumericFrames.Position.X).Should().BeGreaterThan(2.9,
            "the crate is in front of the hull, which did not pass through it");
        withNumericFrames.Ticks.Should().Be(withoutNumericFrames.Ticks, "the ghost is at the same place");
        (withNumericFrames.Position - withoutNumericFrames.Position).Length.Should().BeLessThan(1e-3, "the ghost pushed the crate to the same place");
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2, "the ghost moves at the same velocity");
    }

    /// <summary>
    /// A ghost follows the chord of its real body: Jolt's velocity over the last step, which is also the velocity that the
    /// World reports. In uniform gravity of 9.81 m/s² along -Y, the ship's falling frame accelerates, so its instant
    /// velocity at the end of a step is half a step of gravity, about 0.08 m/s, faster than its chord.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheGhostOfAFallingShipMovesInAParkedStationWithTheShipsVelocityOverTheLastStep(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(StationCenter, DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(StationRadius), MotionDriver.Static),
        }));
        // 67 m from the station's center, so the bubbles overlap from the start.
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(1060, 30),
            LinearVelocity = new Vector3(-ShipSpeed, 0, 0),
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(ShipRadius), MotionDriver.Physics),
        });
        Tick(world, 3);
        FrameHandle home = world.PhysicsHomeOf(shipOwner);
        if (numericFrames)
            world.Frames.Numeric(home).Should().Be(NumericMode.Falling);
        else
            home.Should().Be(space);

        JoltPhysicsBackend.BodyFacts ghost = faults.Jolt.BodiesIn(station)
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, shipOwner));
        Double3 velocity = world.VelocityIn(shipOwner, station);

        velocity.Y.Should().BeLessThan(-0.4, "the ship has fallen for 3 steps");
        ((Double3)ghost.LinearVelocity - velocity).Length.Should().BeLessThan(1e-4, "the ghost moves with the ship's chord");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Two ships at rest 50 m apart in uniform gravity of 9.81 m/s² along -Y, each a hull that physics drives with a bubble
    /// of 30 m, fall side by side. Neither fits in the other, so each has a ghost in the other. The frame of a ship moves
    /// with its hull's chord, so the ghost of the other ship is at rest in it, as the other ship is. The hull's instant
    /// velocity at the end of a step is half a step of gravity, about 0.08 m/s, faster than its chord.
    /// </summary>
    [Fact]
    public void TheGhostOfAShipBesideAFallingShipMovesInItWithItsVelocityOverTheLastStep()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        Entity leftOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = DPose.Identity,
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(ShipRadius), MotionDriver.Physics),
        });
        Entity rightOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(50),
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(ShipRadius), MotionDriver.Physics),
        });
        FrameHandle left = world.GetOwnedFrame(leftOwner);
        Tick(world, 3);
        world.PhysicsHomeOf(rightOwner).Should().Be(space, "the ships are slow and near the origin, so they get no numeric frame");

        JoltPhysicsBackend.BodyFacts ghost = faults.Jolt.BodiesIn(left)
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, rightOwner));
        Double3 velocity = world.VelocityIn(rightOwner, left);

        world.VelocityIn(rightOwner, space).Y.Should().BeLessThan(-0.4, "the ships have fallen for 3 steps");
        ((Double3)ghost.LinearVelocity - velocity).Length.Should().BeLessThan(1e-4, "the ghost moves with the chord of the right ship relative to the left one");
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AShipWhoseBubbleStaysAwayFromAParkedStationsHasNoGhost(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        // At its closest the ship's center is 75 m from the station's: its bubble stays 5 m away from the station's.
        StationScene scene = ShipPassingAStation(world, lane: 75);
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames);

        double closest = double.PositiveInfinity;
        for (int tick = 0; tick < 200; tick++)
        {
            world.Tick();
            closest = Math.Min(closest, ShipDistance(world, scene));
            faults.Jolt.GhostCount(scene.ShipOwner).Should().Be(0, $"after tick {tick} the ship's bubble is away from every other bubble");
        }

        closest.Should().BeInRange(75, 75.01, "the ship passed the station");
        world.VelocityIn(scene.Crate, scene.Station).Length.Should().BeLessThan(1e-3, "nothing touched the crate");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The station's hull is an anchor: a hull of a frame that physics does not drive, which never enters a bubble. A ghost
    /// is about touching, not entering, so the hull has a ghost in each semantic child of its semantic frame that its own
    /// bubble overlaps, wherever that child hangs among numeric frames, as it has without them. So it has a ghost in the
    /// ship, which hangs in the ship's falling frame, exactly while their bubbles overlap. The falling frame also keeps the
    /// ghost of the anchor that the numeric update names, because the ship's hull falls in it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AParkedStationsHullHasAGhostInAFastShipExactlyWhileTheirBubblesOverlap(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        StationScene scene = ShipPassingAStation(world, lane: 10);
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames);

        int overlapping = 0;
        bool ghostInTheFallingFrame = false;
        for (int tick = 0; tick < 200; tick++)
        {
            world.Tick();
            double distance = ShipDistance(world, scene);
            bool overlap = distance <= StationRadius + ShipRadius;
            faults.Jolt.HasGhostIn(scene.StationOwner, scene.Ship).Should().Be(overlap,
                $"after tick {tick} the ship's center is {distance:F3} m from the station's, and a hull reaches as far as its own bubble");
            if (overlap)
                overlapping++;
            FrameHandle home = world.PhysicsHomeOf(scene.ShipOwner);
            if (home != scene.Space)
                ghostInTheFallingFrame |= faults.Jolt.HasGhostIn(scene.StationOwner, home);
        }

        overlapping.Should().BeInRange(130, 145, "the bubbles overlap along 138 m of the ship's path");
        ghostInTheFallingFrame.Should().Be(numericFrames, "the ship's falling frame keeps a ghost of the station's hull while the ship's hull passes it");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Runs the station scene with the ship in the lane y = 10 and a crate at (0, -10, 0) in the ship, 10 m from its center
    /// and so deep inside its bubble, until the crate moves at less than 30 m/s relative to the station, at most 150 ticks.
    /// The crate's path in space runs through the station's hull, and the ship's hull passes 6 m beside it. Returns the
    /// ticks that it ran and the crate's state then, relative to the station.
    /// </summary>
    private static (int Ticks, Double3 Position, Double3 Velocity) StopTheShipsCrate(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        StationScene scene = CratesPassingAStation(world, lane: 10, stationCrate: null, shipCrate: At(0, -10));
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames);

        int ticks = TickUntil(world, () => world.VelocityIn(scene.ShipCrate, scene.Station).Length < 30, 150);

        world.Errors.Should().BeEmpty();
        return (ticks, world.PoseIn(scene.ShipCrate, scene.Station).Position, world.VelocityIn(scene.ShipCrate, scene.Station));
    }

    [Fact]
    public void AParkedStationsHullStopsACrateDeepInsideAShipThatFallsPastItAsItDoesWithoutNumericFrames()
    {
        var withNumericFrames = StopTheShipsCrate(numericFrames: true);
        var withoutNumericFrames = StopTheShipsCrate(numericFrames: false);

        // The front of the crate, 1 m ahead of its center, reaches the back of the hull, at x = 2 in the station, after
        // about 97 m, or 1.62 s.
        withoutNumericFrames.Ticks.Should().BeInRange(95, 100, "the ghost of the station's hull in the ship reaches the crate after about 1.62 s");
        withoutNumericFrames.Velocity.Length.Should().BeLessThan(10, "the hull stopped the crate, which bounces off it a little");
        withoutNumericFrames.Position.X.Should().BeGreaterThan(2.9, "the crate is in front of the hull, which did not pass through it");
        withNumericFrames.Ticks.Should().Be(withoutNumericFrames.Ticks,
            "the station's hull has a ghost at the same place in the ship, which hangs in a falling frame");
        (withNumericFrames.Position - withoutNumericFrames.Position).Length.Should().BeLessThan(1e-3, "the ghost stopped the crate at the same place");
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2, "the ghost moves at the same velocity");
    }

    /// <summary>
    /// Runs the station scene with the ship in the lane y = 45, a crate at (0, -30, 0) in the ship, on the edge of its
    /// bubble and so sticking out of it, and a crate at (15, 15, 0) in the station, deep inside it, in the path of the
    /// first one, until the station's crate moves at more than 10 m/s relative to the station, at most 150 ticks. Returns
    /// the ticks that it ran, the state of the station's crate then and the place of the ship's crate, relative to the
    /// station.
    /// </summary>
    private static (int Ticks, Double3 Position, Double3 Velocity, Double3 PusherPosition) PushTheStationsCrate(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        StationScene scene = CratesPassingAStation(world, lane: 45, stationCrate: At(15, 15), shipCrate: At(0, -30));
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames);

        int ticks = TickUntil(world, () => world.VelocityIn(scene.Crate, scene.Station).Length > 10, 150);

        world.Errors.Should().BeEmpty();
        return (ticks, world.PoseIn(scene.Crate, scene.Station).Position, world.VelocityIn(scene.Crate, scene.Station),
            world.PoseIn(scene.ShipCrate, scene.Station).Position);
    }

    [Fact]
    public void ACrateThatSticksOutOfAFallingShipPushesACrateInAParkedStationAsItDoesWithoutNumericFrames()
    {
        var withNumericFrames = PushTheStationsCrate(numericFrames: true);
        var withoutNumericFrames = PushTheStationsCrate(numericFrames: false);

        // The front of the ship's crate reaches the back of the station's, at x = 16 in the station, after about 83 m, or
        // 1.38 s.
        withoutNumericFrames.Ticks.Should().BeInRange(80, 86, "the ghost of the ship's crate in the station reaches the station's crate after about 1.38 s");
        withoutNumericFrames.Velocity.X.Should().BeLessThan(-10, "the ghost pushed the crate along -X");
        (withoutNumericFrames.PusherPosition.X - withoutNumericFrames.Position.X).Should().BeGreaterThan(1.9,
            "the station's crate is in front of the ship's, which did not pass through it");
        withNumericFrames.Ticks.Should().Be(withoutNumericFrames.Ticks,
            "the ship's crate has a ghost at the same place in the station, a semantic sibling of the ship, which hangs in a falling frame");
        (withNumericFrames.Position - withoutNumericFrames.Position).Length.Should().BeLessThan(1e-3, "the ghost pushed the crate to the same place");
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2, "the ghost moves at the same velocity");
    }

    /// <summary>
    /// Runs the station scene with the ship in the lane y = 50, a crate at (0, 40, 0) in the station, on the edge of its
    /// bubble and so sticking out of it, and a crate at (0, -10, 0) in the ship, deep inside its bubble, whose path runs
    /// through the station's crate, until the ship's crate moves at less than 30 m/s relative to the station, at most 150
    /// ticks. The ship's hull passes 7 m beside the station's crate. Returns the ticks that it ran and the state of the
    /// ship's crate then, relative to the station.
    /// </summary>
    private static (int Ticks, Double3 Position, Double3 Velocity) StopTheShipsCrateAtTheStationsCrate(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        StationScene scene = CratesPassingAStation(world, lane: 50, stationCrate: At(0, 40), shipCrate: At(0, -10));
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames);

        int ticks = TickUntil(world, () => world.VelocityIn(scene.ShipCrate, scene.Station).Length < 30, 150);

        world.Errors.Should().BeEmpty();
        return (ticks, world.PoseIn(scene.ShipCrate, scene.Station).Position, world.VelocityIn(scene.ShipCrate, scene.Station));
    }

    [Fact]
    public void ACrateThatSticksOutOfAParkedStationStopsACrateInAFallingShipAsItDoesWithoutNumericFrames()
    {
        var withNumericFrames = StopTheShipsCrateAtTheStationsCrate(numericFrames: true);
        var withoutNumericFrames = StopTheShipsCrateAtTheStationsCrate(numericFrames: false);

        // The front of the ship's crate reaches the back of the station's, at x = 1 in the station, after about 98 m, or
        // 1.63 s.
        withoutNumericFrames.Ticks.Should().BeInRange(95, 101, "the ghost of the station's crate in the ship reaches the ship's crate after about 1.63 s");
        withoutNumericFrames.Velocity.Length.Should().BeLessThan(10, "the ghost, which is at rest in the station, stopped the ship's crate");
        withoutNumericFrames.Position.X.Should().BeGreaterThan(1.9, "the ship's crate is in front of the station's, which did not pass through it");
        withNumericFrames.Ticks.Should().Be(withoutNumericFrames.Ticks,
            "the station's crate has a ghost at the same place in the ship, a semantic sibling of the station, which hangs in a falling frame");
        (withNumericFrames.Position - withoutNumericFrames.Position).Length.Should().BeLessThan(1e-3, "the ghost stopped the crate at the same place");
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2, "the ghost moves at the same velocity");
    }

    /// <summary>
    /// The station scene with the ship in the lane y = 50, a crate at (0, 40, 0) in the station, on the edge of its
    /// bubble, and a crate at (0, -30, 0) in the ship, on the edge of its bubble, whose path runs 20 m from the station's
    /// center. While the ship passes, the station's hull has a ghost in the ship, the ship's crate in the station, and the
    /// station's crate in the ship. Nothing touches.
    /// </summary>
    private static StationScene CratesThatStickOut(World world) =>
        CratesPassingAStation(world, lane: 50, stationCrate: At(0, 40), shipCrate: At(0, -30));

    /// <summary>True when the station's hull has a ghost in the ship, the ship's crate in the station, and the station's crate in the ship.</summary>
    private static bool HasEveryGhost(FaultInjectingBackend faults, StationScene scene) =>
        faults.Jolt.HasGhostIn(scene.StationOwner, scene.Ship) && faults.Jolt.HasGhostIn(scene.ShipCrate, scene.Station)
            && faults.Jolt.HasGhostIn(scene.Crate, scene.Ship);

    /// <summary>
    /// The reconcile at the start of a step drops each ghost that the frame tree does not allow. The test runs the
    /// reconcile alone, by applying an empty change batch after a change of the tree far away, and reads the backend before
    /// the next step, as <see cref="TheGhostOfAShipAtRestInTheFastShipThatHangsInAFallingFrameSurvivesAReconcile"/> does.
    /// </summary>
    [Fact]
    public void TheGhostsOfAStationsHullAndOfCratesThatStickOutBesideAFallingShipSurviveAReconcile()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        StationScene scene = CratesThatStickOut(world);
        FrameHandle buoy = EmptyFrame(world, scene.Space, At(-5000), 5);
        FrameHandle depot = EmptyFrame(world, scene.Space, At(5000), 50);
        TickUntil(world, () => HasEveryGhost(faults, scene), 120);
        HasEveryGhost(faults, scene).Should().BeTrue("after about 1.2 s the crates touch the other bubbles, and the bubbles overlap");
        world.Frames.Numeric(world.Frames.TreeParent(scene.Ship)).Should().Be(NumericMode.Falling);

        // A change far away makes the backend reconcile. The ship is still a semantic child of space.
        world.Frames.Reparent(buoy, depot);
        world.Frames.Update();
        faults.Jolt.ApplyChanges(new PhysicsChanges(), world.Frames);

        faults.Jolt.HasGhostIn(scene.StationOwner, scene.Ship).Should().BeTrue("the station's hull touches the ship's bubble, a semantic child of the hull's frame");
        faults.Jolt.HasGhostIn(scene.ShipCrate, scene.Station).Should().BeTrue(
            "the ship's crate sticks out of the ship into its tree parent, a falling frame, and touches the bubble of the station, a semantic child of that frame's semantic frame");
        faults.Jolt.HasGhostIn(scene.Crate, scene.Ship).Should().BeTrue(
            "the station's crate sticks out of the station into space, and touches the bubble of the ship, a semantic child of space");
        world.Tick();
        HasEveryGhost(faults, scene).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>True when the last numeric update named a ghost of the anchor <paramref name="anchor"/> in <paramref name="frame"/>.</summary>
    private static bool NamesAnchorGhost(World world, Entity anchor, FrameHandle frame)
    {
        foreach (NumericGhost ghost in world.Frames.AnchorGhosts)
            if (ghost.Anchor == anchor && ghost.Frame == frame)
                return true;
        return false;
    }

    /// <summary>
    /// The reconcile allows a ghost only in the frames where the ghost pass can put one. A falling frame keeps a ghost of
    /// the station's hull, an anchor, only while the numeric update names it: while the merge test links the ship's hull to
    /// the station's, which reaches the two hulls' bounding spheres plus 2 s of their relative speed, about 127 m. The
    /// falling frame is a child of space, the frame of the station's hull, but it has no bubble. So the reconcile after the
    /// update stops naming the ghost drops it before the next step, which would otherwise run with it.
    /// </summary>
    [Fact]
    public void AReconcileDropsTheGhostOfAnAnchorThatTheNumericUpdateNoLongerNames()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        StationScene scene = CratesPassingAStation(world, lane: 10, stationCrate: null, shipCrate: null);
        FrameHandle buoy = EmptyFrame(world, scene.Space, At(-5000), 5);
        FrameHandle depot = EmptyFrame(world, scene.Space, At(5000), 50);
        world.Tick();
        FrameHandle falling = world.PhysicsHomeOf(scene.ShipOwner);
        world.Frames.Numeric(falling).Should().Be(NumericMode.Falling);
        NamesAnchorGhost(world, scene.StationOwner, falling).Should().BeTrue("the ship's hull starts about 100 m from the station's");

        TickUntil(world, () => !NamesAnchorGhost(world, scene.StationOwner, falling), 300);
        NamesAnchorGhost(world, scene.StationOwner, falling).Should().BeFalse("the ship's hull flies on until it is 127 m past the station's");
        world.PhysicsHomeOf(scene.ShipOwner).Should().Be(falling, "the ship's hull still falls in the same frame");
        faults.Jolt.HasGhostIn(scene.StationOwner, falling).Should().BeTrue("the last step ran with the ghost that the update named before it");

        // A change far away makes the backend reconcile.
        world.Frames.Reparent(buoy, depot);
        world.Frames.Update();
        faults.Jolt.ApplyChanges(new PhysicsChanges(), world.Frames);

        faults.Jolt.HasGhostIn(scene.StationOwner, falling).Should().BeFalse("no path of the ghost pass leads to a falling frame that keeps no ghost of the anchor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The ghost pass reaches each frame by several paths: the parent of the body's frame, the semantic siblings of that
    /// frame, the semantic children of the body's semantic frame, and the falling frames that keep a ghost of an anchor.
    /// No frame gets a second ghost of a body, or a ghost beside its real body, or a ghost of the hull that owns it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoSystemHoldsTwoBodiesOfOneEntityWhileCratesThatStickOutPassEachOther(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        StationScene scene = CratesThatStickOut(world);
        Entity[] entities = [scene.StationOwner, scene.Crate, scene.ShipOwner, scene.ShipCrate];
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames);

        bool sawEveryGhost = false;
        for (int tick = 0; tick < 200; tick++)
        {
            world.Tick();
            sawEveryGhost |= HasEveryGhost(faults, scene);
            FrameHandle[] systems = [scene.Space, scene.Station, scene.Ship, world.PhysicsHomeOf(scene.ShipOwner)];
            foreach (FrameHandle frame in systems.Distinct())
            {
                List<JoltPhysicsBackend.BodyFacts> bodies = faults.Jolt.BodiesIn(frame);
                foreach (Entity entity in entities)
                    bodies.Count(facts => BodyUserData.Names(facts.UserData, entity)).Should().BeLessThanOrEqualTo(1,
                        $"after tick {tick} the system of {frame} holds the real body of {entity} or one ghost of it, or neither");
            }
            faults.Jolt.HasGhostIn(scene.StationOwner, scene.Station).Should().BeFalse("a hull has no ghost in its own frame");
            faults.Jolt.HasGhostIn(scene.ShipOwner, scene.Ship).Should().BeFalse("a hull has no ghost in its own frame");
        }

        sawEveryGhost.Should().BeTrue("the ghosts of every path were there at once while the ship passed");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The station scene with the ship in the lane y = 75, so that the ship's bubble stays 5 m away from the station's. The
    /// station's crate at (0, 40, 0) and the ship's crate at (0, -30, 0) stick out of their frames' bubbles, toward the other
    /// frame, but each stays at least 5 m away from the other bubble. Each crate has a ghost only in the systems of its
    /// frame's parent: the ship's crate in the ship's falling frame and in space, the ship's semantic parent, which are one
    /// frame without numeric frames. The station's crate has a ghost in space, and while the ship's hull may touch it, one in
    /// the hull's falling frame, a numeric frame of space, whose member meets the ghost in space without numeric frames
    /// (ticket ter-2gg0).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BodiesThatStayAwayFromABubbleHaveNoGhostThereButOneInEachSystemOfTheirFramesParent(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        StationScene scene = CratesPassingAStation(world, lane: 75, stationCrate: At(0, 40), shipCrate: At(0, -30));
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames);

        double closest = double.PositiveInfinity;
        bool sawGhostInHullsFrame = false;
        for (int tick = 0; tick < 200; tick++)
        {
            world.Tick();
            closest = Math.Min(closest, ShipDistance(world, scene));
            faults.Jolt.HasGhostIn(scene.StationOwner, scene.Ship).Should().BeFalse($"after tick {tick} the ship's bubble is beyond the reach of the station's hull");
            faults.Jolt.HasGhostIn(scene.ShipCrate, scene.Station).Should().BeFalse($"after tick {tick} the ship's crate is away from the station's bubble");
            faults.Jolt.HasGhostIn(scene.Crate, scene.Ship).Should().BeFalse($"after tick {tick} the station's crate is away from the ship's bubble");
            faults.Jolt.GhostCount(scene.ShipCrate).Should().Be(numericFrames ? 2 : 1,
                "the ship's crate sticks out of the ship, so it has a ghost in the ship's tree parent and one in space, the ship's semantic parent, which is the tree parent without numeric frames");
            faults.Jolt.HasGhostIn(scene.ShipCrate, world.Frames.TreeParent(scene.Ship)).Should().BeTrue();
            faults.Jolt.HasGhostIn(scene.ShipCrate, scene.Space).Should().BeTrue();
            faults.Jolt.HasGhostIn(scene.Crate, scene.Space).Should().BeTrue("the station's crate sticks out of the station into space");
            bool inHullsFrame = numericFrames && faults.Jolt.HasGhostIn(scene.Crate, world.PhysicsHomeOf(scene.ShipOwner));
            sawGhostInHullsFrame |= inHullsFrame;
            faults.Jolt.GhostCount(scene.Crate).Should().Be(inHullsFrame ? 2 : 1,
                "the station's crate has a ghost in space, and one in the falling frame of the ship's hull while the hull may touch it");
        }

        sawGhostInHullsFrame.Should().Be(numericFrames, "the ship's hull passes within 2 s at 60 m/s of the station's crate");

        closest.Should().BeInRange(75, 75.01, "the ship passed the station");
        world.VelocityIn(scene.Crate, scene.Station).Length.Should().BeLessThan(1e-3, "nothing touched the station's crate");
        world.VelocityIn(scene.ShipCrate, scene.Ship).Length.Should().BeLessThan(1e-3, "nothing touched the ship's crate");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ghost follows the chord of its real body, as
    /// <see cref="TheGhostOfAFallingShipMovesInAParkedStationWithTheShipsVelocityOverTheLastStep"/> shows for a hull. In
    /// uniform gravity of 9.81 m/s² along -Y, the ship falls past the station from (1040, 30, 0). A crate at (-30, 0, 0) in
    /// the ship sticks out of the ship's bubble and into the station's, and a crate at (24, 32, 0) in the station sticks
    /// out of the station's bubble and into the ship's. The ship's falling frame and its hull accelerate, so their instant
    /// velocities at the end of a step are half a step of gravity, about 0.08 m/s, faster than their chords.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheGhostsOfCratesThatStickOutBesideAFallingShipMoveWithTheirVelocitiesOverTheLastStep(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        StationScene scene = CratesPassingAStation(world, lane: 30, stationCrate: At(24, 32), shipCrate: At(-30), shipX: 1040,
            gravity: GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Tick(world, 3);
        ShipHomeShouldBe(world, scene, numericFrames);

        JoltPhysicsBackend.BodyFacts shipCratesGhost = GhostOf(faults, scene.ShipCrate, scene.Station);
        JoltPhysicsBackend.BodyFacts stationCratesGhost = GhostOf(faults, scene.Crate, scene.Ship);
        Double3 shipCratesVelocity = world.VelocityIn(scene.ShipCrate, scene.Station);
        Double3 stationCratesVelocity = world.VelocityIn(scene.Crate, scene.Ship);

        shipCratesVelocity.Y.Should().BeLessThan(-0.4, "the ship and its crate have fallen for 3 steps");
        ((Double3)shipCratesGhost.LinearVelocity - shipCratesVelocity).Length.Should().BeLessThan(1e-4,
            "the ghost of the ship's crate moves in the station with the crate's chord");
        ((Double3)stationCratesGhost.LinearVelocity - stationCratesVelocity).Length.Should().BeLessThan(1e-4,
            "the ghost of the station's crate moves in the ship with the crate's chord relative to the ship");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>The one ghost of <paramref name="entity"/> in the system of <paramref name="frame"/>.</summary>
    private static JoltPhysicsBackend.BodyFacts GhostOf(FaultInjectingBackend faults, Entity entity, FrameHandle frame) =>
        faults.Jolt.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, entity));

    private sealed record ShipsScene(FrameHandle Space, Entity SlowOwner, FrameHandle Slow, Entity FastOwner, FrameHandle Fast);

    /// <summary>
    /// Space, and at its origin a ship at rest: a hull that physics drives, with a bubble of radius 100 m. A fast ship
    /// flies past at 60 m/s along -X from (150, 140, 0): a hull that physics drives, with a bubble of radius 60 m. Neither
    /// ship fits in the other. Their bubbles overlap while their centers are at most 160 m apart, but the merge test,
    /// which reaches the hulls' radii plus 2 s of their relative speed, about 127 m, never links them, so the slow ship stays
    /// in the system of space when the fast one falls in a numeric frame of its own.
    /// </summary>
    private static ShipsScene ShipPassingAShip(World world)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity slowOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = DPose.Identity,
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Physics),
        });
        Entity fastOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(150, 140),
            LinearVelocity = new Vector3(-ShipSpeed, 0, 0),
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(60), MotionDriver.Physics),
        });
        return new ShipsScene(space, slowOwner, world.GetOwnedFrame(slowOwner), fastOwner, world.GetOwnedFrame(fastOwner));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoShipsThatDoNotFitInEachOtherHaveGhostsInEachOtherExactlyWhileTheirBubblesOverlap(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        ShipsScene scene = ShipPassingAShip(world);
        world.Tick();

        int overlapping = 0;
        for (int tick = 0; tick < 260; tick++)
        {
            world.Tick();
            if (numericFrames)
            {
                world.Frames.Numeric(world.PhysicsHomeOf(scene.FastOwner)).Should().Be(NumericMode.Falling);
                world.PhysicsHomeOf(scene.SlowOwner).Should().Be(scene.Space, "the merge test never links the two ships");
            }
            double distance = (world.PoseIn(scene.FastOwner, scene.Space).Position - world.PoseIn(scene.SlowOwner, scene.Space).Position).Length;
            bool overlap = distance <= 160;
            faults.Jolt.HasGhostIn(scene.FastOwner, scene.Slow).Should().Be(overlap,
                $"after tick {tick} the fast ship's center is {distance:F3} m from the slow ship's");
            faults.Jolt.HasGhostIn(scene.SlowOwner, scene.Fast).Should().Be(overlap,
                $"after tick {tick} the slow ship's center is {distance:F3} m from the fast ship's");
            if (overlap)
                overlapping++;
        }

        overlapping.Should().BeInRange(150, 160, "the bubbles overlap along 155 m of the fast ship's path");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>A frame with no body, in <paramref name="parent"/>, at <paramref name="pose"/>, with a bubble of <paramref name="radius"/> m.</summary>
    private static FrameHandle EmptyFrame(World world, FrameHandle parent, DPose pose, float radius) =>
        world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = parent, Pose = pose, AsFrame = new FrameSpec(FrameBounds.Sphere(radius), MotionDriver.Static) }));

    /// <summary>
    /// The reconcile at the start of a step drops each ghost that the frame tree does not allow, and the ghost pass at the
    /// end of the step makes a missing ghost again. So after a tick the ghost would be there even if the reconcile had
    /// dropped it, and the step would have run without it. The test runs the reconcile alone, as ReconcileTests does, by
    /// applying an empty change batch, and reads the backend before the next step.
    /// </summary>
    [Fact]
    public void TheGhostOfAShipAtRestInTheFastShipThatHangsInAFallingFrameSurvivesAReconcile()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        ShipsScene scene = ShipPassingAShip(world);
        FrameHandle buoy = EmptyFrame(world, scene.Space, At(-5000), 5);
        FrameHandle depot = EmptyFrame(world, scene.Space, At(5000), 50);
        TickUntil(world, () => faults.Jolt.HasGhostIn(scene.SlowOwner, scene.Fast), 120);
        faults.Jolt.HasGhostIn(scene.SlowOwner, scene.Fast).Should().BeTrue("the bubbles overlap after about 1.2 s");
        world.Frames.Numeric(world.Frames.TreeParent(scene.Fast)).Should().Be(NumericMode.Falling);

        // A change far away makes the backend reconcile. The fast ship is still a semantic child of space.
        world.Frames.Reparent(buoy, depot);
        world.Frames.Update();
        faults.Jolt.ApplyChanges(new PhysicsChanges(), world.Frames);

        faults.Jolt.HasGhostIn(scene.SlowOwner, scene.Fast).Should().BeTrue(
            "without the ghost, the fast ship's contents would pass through the slow ship's hull for a step");
        faults.Jolt.HasGhostIn(scene.FastOwner, scene.Slow).Should().BeTrue();
        world.Tick();
        faults.Jolt.HasGhostIn(scene.SlowOwner, scene.Fast).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }

    // ---- The ghost in the semantic parent (ticket ter-wllb) ----
    //
    // A body that sticks out of a ship whose tree parent is a numeric frame is partly in the ship's semantic parent, as it
    // is without numeric frames. The numeric frame holds only the bodies that the merge test links to the ship's hull. The
    // other bodies of the semantic parent stay in its own system, so the body that sticks out has a ghost there too. A
    // ghost is kinematic, so only a dynamic body collides with it: the engine never asks Jolt for contacts between a
    // kinematic body and a static one, with numeric frames or without. So the rocks of these scenes are dynamic balls.

    /// <summary>The radius of the big ship's bubble, in meters.</summary>
    private const float BigShipRadius = 200;

    /// <summary>A rock: a ball of radius 0.5 m and 1 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc RockBody = new(new SphereShapeDesc(0.5f), Mass: 1, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>The rock scene.</summary>
    /// <param name="Rock">The rock in space, or null when the scene has none.</param>
    private sealed record RockScene(FrameHandle Space, Entity ShipOwner, FrameHandle Ship, Entity Crate, Entity Rock);

    /// <summary>
    /// Space, with the field <paramref name="gravity"/> or none, and in it a ship: a hull of 4 m by 4 m by 4 m and 1000 kg
    /// that physics drives, at <paramref name="shipPose"/> with the velocity <paramref name="shipVelocity"/>, with a bubble
    /// of radius 200 m. A crate of 2 m by 2 m by 2 m and 2 kg floats at <paramref name="cratePose"/> in the ship. A rock
    /// (see <see cref="RockBody"/>) flies at <paramref name="rockVelocity"/> from <paramref name="rockPose"/> in space, or
    /// the scene has none.
    /// </summary>
    private static RockScene ShipAndRock(World world, DPose shipPose, Double3 shipVelocity, DPose cratePose, DPose? rockPose,
        Double3 rockVelocity = default, GravityField gravity = default)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static, gravity));
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = shipPose,
            LinearVelocity = shipVelocity,
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(BigShipRadius), MotionDriver.Physics),
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = cratePose, Body = CrateBody });
        Entity rock = rockPose is { } pose
            ? world.Spawn(new SpawnDesc { Frame = space, Pose = pose, LinearVelocity = rockVelocity, Body = RockBody })
            : Entity.Null;
        return new RockScene(space, shipOwner, ship, crate, rock);
    }

    /// <summary>
    /// The ship flies at 60 m/s along -X from (1100, 0, 0) in space, so its hull falls in a numeric frame of its own. The
    /// crate floats at (0, -200, 0) in the ship, on the edge of its bubble, and so sticks out of it, or at
    /// (0, <paramref name="crateY"/>, 0). The rock rests at (1000, -200.6, 0) in space, in the path of the part of the crate
    /// that sticks out, 0.1 m outside the ship's bubble, or the scene has none. The merge test reaches the bounding spheres
    /// of the hull and the rock, plus 2 s of their relative speed, plus a margin of 10 m, about 134 m in all. The rock stays
    /// at least 200 m from the hull, so the merge test never links them, and the rock stays in the system of space.
    /// </summary>
    private static RockScene FallingShipPassingARock(World world, bool withRock = true, double crateY = -BigShipRadius,
        GravityField gravity = default) =>
        ShipAndRock(world, At(1100), new Double3(-ShipSpeed, 0, 0), At(0, crateY), withRock ? At(1000, -BigShipRadius - 0.6) : null,
            gravity: gravity);

    /// <summary>
    /// The ship rests at (10150, 0, 0) in space, 10.15 km from its origin, so its hull rests in a numeric frame of its own.
    /// The crate floats at (-200, 0, 0) in the ship, on the edge of its bubble, and so sticks out of it, or at
    /// (<paramref name="crateX"/>, 0, 0). The rock flies at 10 m/s along +X from (9930, 0, 0) in space at the crate, or the
    /// scene has none. The rock is less than 10 km from the origin of space and slow, so it needs no numeric frame. The
    /// merge test reaches the bounding spheres of the hull and the rock plus 2 s of their relative speed, about 24 m, so it
    /// never links the rock to the hull, 200 m away. The face of the crate that points at the rock is 1 m outside the
    /// bubble, so the rock meets it before the rock touches the bubble.
    /// </summary>
    private static RockScene ShipRestingBesideARock(World world, bool withRock = true, double crateX = -BigShipRadius) =>
        ShipAndRock(world, At(10_150), Double3.Zero, At(crateX), withRock ? At(9_930) : null, rockVelocity: new Double3(10, 0, 0));

    /// <summary>
    /// Checks that the ship's hull is in a numeric frame of <paramref name="mode"/> with numeric frames, and in the system of
    /// space without.
    /// </summary>
    private static void ShipHomeShouldBe(World world, RockScene scene, bool numericFrames, NumericMode mode)
    {
        FrameHandle home = world.PhysicsHomeOf(scene.ShipOwner);
        if (numericFrames)
        {
            world.Frames.Numeric(home).Should().Be(mode);
            world.Frames.TreeParent(home).Should().Be(scene.Space);
            world.Frames.TreeParent(scene.Ship).Should().Be(home, "the ship's frame hangs under the numeric frame of its hull");
        }
        else
        {
            home.Should().Be(scene.Space);
        }
    }

    /// <summary>
    /// Runs the rock scene of <paramref name="build"/>, whose ship's hull rests or falls as <paramref name="mode"/> says,
    /// until <paramref name="done"/> holds for the rock's velocity relative to space, at most 200 ticks. Returns the ticks
    /// that it ran, the state of the rock then relative to space, and whether the rock is still in the system of space.
    /// </summary>
    private static (int Ticks, Double3 Position, Double3 Velocity, bool InSpace) RunTheRock(bool numericFrames, Func<World, RockScene> build,
        NumericMode mode, Func<Double3, bool> done)
    {
        using World world = NewWorld(numericFrames, out _);
        RockScene scene = build(world);
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames, mode);
        world.PhysicsHomeOf(scene.Rock).Should().Be(scene.Space, "the rock needs no numeric frame, and the merge test does not link it to the hull");

        int ticks = TickUntil(world, () => done(world.VelocityIn(scene.Rock, scene.Space)), 200);

        world.Errors.Should().BeEmpty();
        return (ticks, world.PoseIn(scene.Rock, scene.Space).Position, world.VelocityIn(scene.Rock, scene.Space),
            world.GetFrame(scene.Rock) == scene.Space);
    }

    [Fact]
    public void ACrateThatSticksOutOfAFallingShipPushesARockInSpaceThatTheMergeTestDoesNotLinkToTheHullAsItDoesWithoutNumericFrames()
    {
        Func<World, RockScene> build = world => FallingShipPassingARock(world);
        var withNumericFrames = RunTheRock(numericFrames: true, build, NumericMode.Falling, velocity => velocity.Length > 10);
        var withoutNumericFrames = RunTheRock(numericFrames: false, build, NumericMode.None, velocity => velocity.Length > 10);

        // The front of the crate reaches the back of the rock, at x = 1000.5 in space, after 98.5 m, or 1.64 s.
        withoutNumericFrames.Ticks.Should().BeInRange(96, 102, "the ghost of the crate in space reaches the rock after about 1.64 s");
        withoutNumericFrames.Velocity.X.Should().BeLessThan(-10, "the ghost pushed the rock along -X");
        withoutNumericFrames.InSpace.Should().BeTrue("the rock never touched the ship's bubble");
        withNumericFrames.Ticks.Should().Be(withoutNumericFrames.Ticks,
            "the crate also has a ghost in the system of space, the ship's semantic parent, although the ship hangs in a falling frame");
        (withNumericFrames.Position - withoutNumericFrames.Position).Length.Should().BeLessThan(1e-3, "the ghost pushed the rock to the same place");
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2, "the ghost moves at the same velocity");
        withNumericFrames.InSpace.Should().BeTrue();
    }

    [Fact]
    public void ACrateThatSticksOutOfAShipThatRestsFarOutStopsARockInSpaceAsItDoesWithoutNumericFrames()
    {
        Func<World, RockScene> build = world => ShipRestingBesideARock(world);
        var withNumericFrames = RunTheRock(numericFrames: true, build, NumericMode.Resting, velocity => velocity.Length < 1);
        var withoutNumericFrames = RunTheRock(numericFrames: false, build, NumericMode.None, velocity => velocity.Length < 1);

        // The front of the rock reaches the face of the crate, at x = 9949 in space, after 18.5 m, or 1.85 s.
        withoutNumericFrames.Ticks.Should().BeInRange(108, 114, "the rock reaches the ghost of the crate in space after about 1.85 s");
        withoutNumericFrames.Position.X.Should().BeLessThan(9_948.6, "the rock stopped at the ghost, which did not let it through");
        withoutNumericFrames.InSpace.Should().BeTrue("the rock stopped before it touched the ship's bubble");
        withNumericFrames.Ticks.Should().Be(withoutNumericFrames.Ticks,
            "the crate also has a ghost in the system of space, the ship's semantic parent, although the ship hangs in a resting frame");
        (withNumericFrames.Position - withoutNumericFrames.Position).Length.Should().BeLessThan(1e-3, "the ghost stopped the rock at the same place");
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2);
        withNumericFrames.InSpace.Should().BeTrue();
    }

    /// <summary>
    /// The ship rests at (10150, 0, 0) in space, so its hull rests in a numeric frame, under which the ship's frame hangs.
    /// The crate floats at (-200, 0, 0) in the ship, on the edge of its bubble, and drifts outward along -X at 2 m/s. A rock
    /// (a ball of radius 0.5 m and 1 kg, slowed by its damping) rests in space 0.1 m in front of the crate's face, outside
    /// the bubble. The crate's ghost in space, the ship's semantic parent, reaches the rock at once and pushes it ahead.
    /// When the crate is fully outside the bubble plus the exit margin, it leaves the ship into the ship's tree parent, the
    /// numeric frame, while it still pushes the rock, whose body is in the system of space. Without numeric frames, the
    /// crate leaves into space, and its body there takes over the contact of its ghost. With them, the sync point after
    /// the hand-off links the two bodies and moves the rock into the numeric frame, and the next step finds their contact
    /// there. In both runs, each entity hears one Collision and no end (review of ter-ejx9).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACrateThatLeavesAShipInARestingFrameWhileItPushesARockInSpaceKeepsTheContactAsItDoesWithoutNumericFrames(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(10_150),
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(BigShipRadius), MotionDriver.Physics),
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        var crateLog = new CollisionLog();
        var rockLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = ship, Pose = At(-BigShipRadius), LinearVelocity = new Double3(-2, 0, 0), Body = CrateBody, Components = [crateLog],
        });
        // The crate's face is at x = 9949 in space, and the rock's side at x = 9948.9.
        Entity rock = world.Spawn(new SpawnDesc
        {
            Frame = space, Pose = At(9_948.4), Body = RockBody with { LinearDamping = 0.5f }, Components = [rockLog],
        });

        int handOff = TickUntil(world, () => world.GetFrame(crate) == space, 400);

        world.GetFrame(crate).Should().Be(space, "the crate left the ship's bubble");
        if (numericFrames)
        {
            world.Frames.Numeric(world.PhysicsHomeOf(crate)).Should().Be(NumericMode.Resting,
                "the crate left into the ship's tree parent, and the sync point linked it to the rock there");
            faults.Outputs[^1].HandOffs.Should().Contain(h => h.Entity == crate && world.Frames.IsNumeric(h.To));
            // The contact is in no system until the next step finds it in the numeric frame, and the ledger still counts it.
            faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        }
        Tick(world, 60);

        handOff.Should().BeInRange(120, 250, "the crate needs about 3 s to leave the bubble plus the exit margin");
        crateLog.NewsOf(rock).Should().Equal([ContactNews.Started], "the crate pushes the rock the whole time");
        rockLog.NewsOf(crate).Should().Equal([ContactNews.Started]);
        world.VelocityIn(rock, space).X.Should().BeLessThan(-0.1, "the crate still pushes the rock");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The reconcile at the start of a step drops each ghost that the frame tree does not allow. The test runs the
    /// reconcile alone, by applying an empty change batch after a change of the tree far away, and reads the backend before
    /// the next step, as <see cref="TheGhostOfAShipAtRestInTheFastShipThatHangsInAFallingFrameSurvivesAReconcile"/> does.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheGhostInSpaceOfACrateThatSticksOutOfAShipInANumericFrameSurvivesAReconcile(bool shipRests)
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        RockScene scene = shipRests ? ShipRestingBesideARock(world, withRock: false) : FallingShipPassingARock(world, withRock: false);
        FrameHandle buoy = EmptyFrame(world, scene.Space, At(-5000), 5);
        FrameHandle depot = EmptyFrame(world, scene.Space, At(5000), 50);
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames: true, shipRests ? NumericMode.Resting : NumericMode.Falling);
        FrameHandle home = world.PhysicsHomeOf(scene.ShipOwner);
        faults.Jolt.HasGhostIn(scene.Crate, scene.Space).Should().BeTrue("the crate sticks out of the ship");
        faults.Jolt.HasGhostIn(scene.Crate, home).Should().BeTrue();

        // A change far away makes the backend reconcile. The ship is still a semantic child of space.
        world.Frames.Reparent(buoy, depot);
        world.Frames.Update();
        faults.Jolt.ApplyChanges(new PhysicsChanges(), world.Frames);

        faults.Jolt.HasGhostIn(scene.Crate, scene.Space).Should().BeTrue(
            "the crate sticks out of the ship, so it is partly in space, the ship's semantic parent, whose own system holds the bodies there that the merge test does not link to the hull");
        faults.Jolt.HasGhostIn(scene.Crate, home).Should().BeTrue("the crate is partly in the ship's tree parent too");
        world.Tick();
        faults.Jolt.HasGhostIn(scene.Crate, scene.Space).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A crate 100 m inside the edge of the ship's bubble touches no bubble boundary, and the scene has no other bubble, so it
    /// has no ghost: not in the ship's tree parent, and not in space.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ACrateFarFromEveryBubbleBoundaryHasNoGhostWithNumericFramesOrWithout(bool numericFrames, bool shipRests)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        RockScene scene = shipRests
            ? ShipRestingBesideARock(world, withRock: false, crateX: -BigShipRadius + 100)
            : FallingShipPassingARock(world, withRock: false, crateY: -BigShipRadius + 100);
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames, shipRests ? NumericMode.Resting : NumericMode.Falling);

        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            faults.Jolt.GhostCount(scene.Crate).Should().Be(0, $"after tick {tick} the crate is 100 m inside the ship's bubble");
        }
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ghost follows the chord of its real body, the change of its position over the last step divided by the step.
    /// In uniform gravity of 9.81 m/s² along -Y, the ship falls with its crate at (0, -200, 0), which sticks out of its
    /// bubble. The ship's falling frame accelerates, so its instant velocity at the end of a step is half a step of gravity,
    /// about 0.08 m/s, faster than its chord.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheGhostInSpaceOfACrateThatSticksOutOfAFallingShipHasTheCratesPlaceAndItsVelocityOverTheLastStep(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        RockScene scene = FallingShipPassingARock(world, withRock: false, gravity: GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Tick(world, 3);
        ShipHomeShouldBe(world, scene, numericFrames, NumericMode.Falling);

        JoltPhysicsBackend.BodyFacts ghost = GhostOf(faults, scene.Crate, scene.Space);
        Double3 velocity = world.VelocityIn(scene.Crate, scene.Space);

        velocity.Y.Should().BeLessThan(-0.4, "the ship and its crate have fallen for 3 steps");
        (ghost.Position - world.PoseIn(scene.Crate, scene.Space).Position).Length.Should().BeLessThan(1e-6,
            "the ghost is where the crate is in space");
        ((Double3)ghost.LinearVelocity - velocity).Length.Should().BeLessThan(1e-4, "the ghost moves in space with the crate's chord");
        world.Errors.Should().BeEmpty();
    }
}
