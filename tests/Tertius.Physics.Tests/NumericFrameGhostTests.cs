using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A body whose physics home is a numeric frame has a ghost in the bubble of each semantic frame that it touches without
/// entering, as it has without numeric frames (ticket ter-ip1k). A ghost is a kinematic copy of a body in the physics
/// system of another frame, so that the bodies there collide with it. A numeric frame has no bubble, so it does not change
/// which bubbles a body meets. A frame's hull enters another frame only if it fits there: its bubble is less than half as
/// wide as the other one. A hull that does not fit has a ghost in the other frame while the two bubbles overlap. These
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

    private sealed record StationScene(FrameHandle Space, Entity StationOwner, FrameHandle Station, Entity Crate, Entity ShipOwner, FrameHandle Ship);

    /// <summary>
    /// Space, and 1 km along +X in it a parked station: a frame that physics does not drive, with a bubble of radius 40 m
    /// around a static hull of 4 m by 4 m by 4 m at its center. The hull is an anchor in the system of space. A crate of
    /// 2 m by 2 m by 2 m and 2 kg floats in the station at (15, 10, 0). A ship flies past at 60 m/s along -X from
    /// (1100, <paramref name="lane"/>, 0) in space: a hull that physics drives, with a bubble of radius 30 m, which does
    /// not fit in the station's.
    /// </summary>
    private static StationScene ShipPassingAStation(World world, double lane)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity stationOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(StationCenter, DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(StationRadius), MotionDriver.Static),
        });
        FrameHandle station = world.GetOwnedFrame(stationOwner);
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = station,
            Pose = At(15, 10),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
        });
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(1100, lane),
            LinearVelocity = new Vector3(-ShipSpeed, 0, 0),
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(ShipRadius), MotionDriver.Physics),
        });
        return new StationScene(space, stationOwner, station, crate, shipOwner, world.GetOwnedFrame(shipOwner));
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

    [Fact]
    public void AParkedStationsHullKeepsTheGhostsOfAnAnchorWhenAFastShipPassesIt()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        StationScene scene = ShipPassingAStation(world, lane: 10);
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames: true);

        // The station's hull is an anchor: a hull of a frame that physics does not drive, which never enters a bubble. Its
        // ghost near a falling body is the one that the numeric update names, in the body's falling frame. It meets only
        // the bubbles of the children of its own frame, so it has no ghost in the ship, which hangs in that falling frame.
        bool ghostInTheFallingFrame = false;
        for (int tick = 0; tick < 200; tick++)
        {
            world.Tick();
            FrameHandle home = world.PhysicsHomeOf(scene.ShipOwner);
            ghostInTheFallingFrame |= faults.Jolt.HasGhostIn(scene.StationOwner, home);
            faults.Jolt.HasGhostIn(scene.StationOwner, scene.Ship).Should().BeFalse($"after tick {tick} the ship hangs in a falling frame");
        }

        ghostInTheFallingFrame.Should().BeTrue("the ship's falling frame keeps a ghost of the station's hull while the ship passes it");
        world.Errors.Should().BeEmpty();
    }

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
