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
/// frame's bubble, which has ghosts in the bubbles of its frame's semantic siblings that it touches. These
/// tests run the World on the real backend, with the default numeric frame options, under which a body that flies at
/// 50 m/s or more relative to its frame gets a falling numeric frame, and with no numeric frames, where the hulls stay in
/// the system of space.
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
    /// frame, but each stays at least 5 m away from the other bubble.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BodiesThatStayAwayFromABubbleHaveNoGhostThere(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        StationScene scene = CratesPassingAStation(world, lane: 75, stationCrate: At(0, 40), shipCrate: At(0, -30));
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames);

        double closest = double.PositiveInfinity;
        for (int tick = 0; tick < 200; tick++)
        {
            world.Tick();
            closest = Math.Min(closest, ShipDistance(world, scene));
            faults.Jolt.HasGhostIn(scene.StationOwner, scene.Ship).Should().BeFalse($"after tick {tick} the ship's bubble is beyond the reach of the station's hull");
            faults.Jolt.HasGhostIn(scene.ShipCrate, scene.Station).Should().BeFalse($"after tick {tick} the ship's crate is away from the station's bubble");
            faults.Jolt.HasGhostIn(scene.Crate, scene.Ship).Should().BeFalse($"after tick {tick} the station's crate is away from the ship's bubble");
            faults.Jolt.GhostCount(scene.ShipCrate).Should().Be(1, "the ship's crate has one ghost, in the tree parent of the ship, out of whose bubble it sticks");
            faults.Jolt.HasGhostIn(scene.ShipCrate, world.Frames.TreeParent(scene.Ship)).Should().BeTrue();
            faults.Jolt.GhostCount(scene.Crate).Should().Be(1, "the station's crate has one ghost, in space");
        }

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
}
