using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A body whose physics home is a numeric frame has a ghost in the bubble of each semantic frame that it touches without
/// entering, as it has without numeric frames (ticket ter-ip1k). A ghost is a kinematic copy of a body in the physics
/// system of another frame, so that the bodies there collide with it. A numeric frame has no bubble, so it does not change
/// which bubbles a body meets. A frame's hull enters another frame only if it fits there: its bubble is less than half as
/// wide as the other one. A hull that does not fit has a ghost in the other frame while the two bubbles overlap. The same
/// rule holds for the hull of a frame that physics does not drive, which is an anchor, and for a body that sticks out of its
/// frame's bubble, which has ghosts in the bubbles of its frame's semantic siblings that it touches. Such a body has one
/// more ghost, in its frame's tree parent, the home of the frame's hull, and none in the system of the semantic parent
/// when that is another frame: the merge test links each body of the semantic parent that may touch the ghost to its
/// place, so the body joins the hull's home and meets the ghost there (ticket ter-4psc). These
/// tests run the World on the real backend, with the default numeric frame options, and with no numeric frames, where the
/// hulls stay in the system of space. Under the default options, a group of free bodies gets a falling numeric frame from
/// 1,000 m/s relative to its frame, a group with a body that thrusts gets one from 50 m/s, and a slow group 2^23 m (about
/// 8,389 km) or more from its frame's origin gets a resting one. So a fast ship of these scenes flies at 60 m/s with a
/// gentle thruster (see <see cref="WarmUpTicks"/>), and a ship that rests lies just beyond 2^23 m from the origin of space
/// (see <see cref="RestDistance"/>).
/// </summary>
public class NumericFrameGhostTests
{
    private const float Dt = 1f / 60f;

    /// <summary>
    /// How many ticks a scene with a fast ship runs before it returns: one fewer than the 30 ticks of thrust (0.5 s, see
    /// <see cref="NumericFrameOptions.ThrustOnset"/>) after which a body thrusts. A hull at 60 m/s falls only when it
    /// thrusts, because a group of free bodies needs 1,000 m/s and a group with a body that thrusts needs 50 m/s. So the
    /// ship carries a gentle thruster (see <see cref="ShipThruster"/>) from its spawn, and it starts
    /// <see cref="WarmUpDistance"/> farther back along its path. The next tick after the scene returns is the ship's 30th
    /// tick of thrust, at whose sync point its hull gets a falling frame, and from then on the ship is where these tests
    /// count from.
    /// </summary>
    private const int WarmUpTicks = 29;

    /// <summary>How far the fast ship flies in the <see cref="WarmUpTicks"/>, in meters: 29 m at 60 m/s.</summary>
    private const double WarmUpDistance = WarmUpTicks * ShipSpeed * Dt;

    /// <summary>
    /// The distance from the origin of space, in meters, from which a slow group gets a resting numeric frame: 2^23 m,
    /// about 8,389 km (see <see cref="NumericFrameOptions.CreateDistance"/>). A ship that rests lies 150 m beyond it, and a
    /// rock beside the ship lies nearer than it, so that the rock gets no numeric frame.
    /// </summary>
    private const double RestDistance = 1 << 23;

    /// <summary>The radius of the station's bubble, in meters.</summary>
    private const float StationRadius = 40;

    /// <summary>The radius of the fast ship's bubble, in meters. It is more than half the station's, so the ship does not fit there.</summary>
    private const float ShipRadius = 30;

    /// <summary>How fast the ship flies along -X in space, in m/s.</summary>
    private const float ShipSpeed = 60;

    private static readonly Double3 StationCenter = new(1000, 0, 0);

    /// <summary>A world on the wrapper with the default numeric frames or, when <paramref name="numericFrames"/> is false, with none.</summary>
    private static World NewWorld(bool numericFrames, out FaultInjectingBackend faults) =>
        NewWorldWithFaults(out faults, configure: numericFrames ? null : options => options.NumericFrames = WithoutNumericFrames);

    /// <summary>
    /// A world on the wrapper for the scenes of a ship that passes a parked station: with the default numeric frames, but a
    /// lookahead of <paramref name="lookahead"/> s, or, when <paramref name="numericFrames"/> is false, with none. The
    /// station's hull is an anchor at rest, so the merge test links a fast body to it, and the body joins the hull's system
    /// (ter-dhkf, docs/plans/numeric-frames.md, "Anchors"). With the default lookahead of 2 s, the ship's hull would join the
    /// system of space 127 m before it reaches the station's hull, before the bubbles overlap, so no ghost of these scenes
    /// would be in a numeric frame. At 0.2 s the merge test reaches 19 m between the two hulls: the ship falls past the
    /// station while their bubbles overlap, except within 16 m of the station's center in the lane y = 10.
    /// </summary>
    private static World NewStationWorld(bool numericFrames, out FaultInjectingBackend faults, double lookahead = 0.2) =>
        NewWorldWithFaults(out faults, configure: options =>
            options.NumericFrames = numericFrames ? new NumericFrameOptions { LookaheadTime = lookahead } : WithoutNumericFrames);

    /// <summary>A hull of 4 m by 4 m by 4 m and 1000 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc ShipHull = new(new BoxShapeDesc(new Vector3(2)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A crate of 2 m by 2 m by 2 m and 2 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc CrateBody = new(new BoxShapeDesc(new Vector3(1)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// The thruster of a fast ship: a push along +X that gives the ship's hull <see cref="Thruster.GentleAcceleration"/>
    /// (1e-4 m/s²) on every tick, from its spawn on. It slows the ship by less than 1 mm/s in the 4 s of a scene, but it makes
    /// the hull a body that thrusts (see <see cref="WarmUpTicks"/>). A scene that runs with numeric frames and without
    /// gives both runs the thruster.
    /// </summary>
    private static Thruster ShipThruster() => new(ShipHull.Mass);

    /// <summary>
    /// Spawns a fast ship in <paramref name="space"/>: a hull that physics drives (see <see cref="ShipHull"/>), with a bubble
    /// of radius <paramref name="radius"/> m and a <see cref="ShipThruster"/>, which flies at 60 m/s along -X. It starts
    /// <see cref="WarmUpDistance"/> farther along +X than <paramref name="at"/>, so that it is at <paramref name="at"/>
    /// after the <see cref="WarmUpTicks"/>, which the caller runs.
    /// </summary>
    private static Entity SpawnFastShip(World world, FrameHandle space, Double3 at, float radius) => world.Spawn(new SpawnDesc
    {
        Frame = space,
        Pose = new DPose(at + new Double3(WarmUpDistance, 0, 0), DQuaternion.Identity),
        LinearVelocity = new Vector3(-ShipSpeed, 0, 0),
        Body = ShipHull,
        AsFrame = new FrameSpec(FrameBounds.Sphere(radius), MotionDriver.Physics),
        Components = [ShipThruster()],
    });

    /// <summary>The station scene.</summary>
    /// <param name="Crate">The crate in the station, or null when the scene has none.</param>
    /// <param name="ShipCrate">The crate in the ship, or null when the scene has none.</param>
    private sealed record StationScene(FrameHandle Space, Entity StationOwner, FrameHandle Station, Entity Crate, Entity ShipOwner, FrameHandle Ship, Entity ShipCrate);

    /// <summary>
    /// Space, and 1 km along +X in it a parked station: a frame that physics does not drive, with a bubble of radius 40 m
    /// around a static hull of 4 m by 4 m by 4 m at its center. The hull is an anchor in the system of space. A crate of
    /// 2 m by 2 m by 2 m and 2 kg floats in the station at (15, 10, 0). A ship flies past at 60 m/s along -X in space: a
    /// hull that physics drives, with a bubble of radius 30 m, which does not fit in the station's, and with a gentle
    /// thruster. The scene returns after the <see cref="WarmUpTicks"/>, with the ship at (1100, <paramref name="lane"/>, 0).
    /// </summary>
    private static StationScene ShipPassingAStation(World world, double lane) => CratesPassingAStation(world, lane, At(15, 10), shipCrate: null);

    /// <summary>
    /// The station and the ship of <see cref="ShipPassingAStation"/>, with a crate of 2 m by 2 m by 2 m and 2 kg at rest at
    /// <paramref name="stationCrate"/> in the station and another at <paramref name="shipCrate"/> in the ship, or no crate
    /// where the place is null. Space has the field <paramref name="gravity"/>, or none. The scene runs the
    /// <see cref="WarmUpTicks"/> before it returns, and the ship is then at (<paramref name="shipX"/>,
    /// <paramref name="lane"/>, 0) in space, less what it fell in the field.
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
        Entity shipOwner = SpawnFastShip(world, space, new Double3(shipX, lane, 0), ShipRadius);
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        Entity cargo = shipCrate is { } inShip ? world.Spawn(new SpawnDesc { Frame = ship, Pose = inShip, Body = CrateBody }) : Entity.Null;
        Tick(world, WarmUpTicks);
        return new StationScene(space, stationOwner, station, crate, shipOwner, ship, cargo);
    }

    /// <summary>The distance in meters from the station's center to the ship's, at the end of the last step.</summary>
    private static double ShipDistance(World world, StationScene scene) =>
        (world.PoseIn(scene.ShipOwner, scene.Space).Position - StationCenter).Length;

    /// <summary>
    /// Checks that the ship's hull is in a falling numeric frame with numeric frames, and in the system of space without.
    /// The scenes check it from the first tick after the <see cref="WarmUpTicks"/> on, the ship's 30th tick of thrust, at
    /// whose sync point the hull gets its falling frame. Most scenes check it right then, when the hull is about 100 m from
    /// the station's.
    /// </summary>
    private static void ShipHomeShouldBe(World world, StationScene scene, bool numericFrames)
    {
        FrameHandle home = world.PhysicsHomeOf(scene.ShipOwner);
        if (numericFrames)
        {
            world.Frames.Numeric(home).Should().Be(NumericMode.Falling,
                "the ship's hull thrusts from its 30th tick of thrust, and at 60 m/s a group with a body that thrusts gets a falling numeric frame");
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
        using World world = NewStationWorld(numericFrames, out FaultInjectingBackend faults);
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
        using World world = NewStationWorld(numericFrames, out FaultInjectingBackend faults);
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
    /// velocity at the end of a step is half a step of gravity, about 0.08 m/s, faster than its chord. The test reads the
    /// ghost 3 ticks after the warm-up. The ship's hull gets its falling frame at the sync point of the first of them, so
    /// it has fallen in its numeric frame for the 2 steps after it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheGhostOfAFallingShipMovesInAParkedStationWithTheShipsVelocityOverTheLastStep(bool numericFrames)
    {
        using World world = NewStationWorld(numericFrames, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(StationCenter, DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(StationRadius), MotionDriver.Static),
        }));
        // After the warm-up and 3 more ticks, the ship is at about (1057, 28.6, 0), 64 m from the station's center, so
        // the bubbles overlap.
        Entity shipOwner = SpawnFastShip(world, space, new Double3(1060, 30, 0), ShipRadius);
        Tick(world, WarmUpTicks + 3);
        FrameHandle home = world.PhysicsHomeOf(shipOwner);
        if (numericFrames)
            world.Frames.Numeric(home).Should().Be(NumericMode.Falling, "the ship's hull thrusts, and at 60 m/s a group with a body that thrusts falls");
        else
            home.Should().Be(space);

        JoltPhysicsBackend.BodyFacts ghost = faults.Jolt.BodiesIn(station)
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, shipOwner));
        Double3 velocity = world.VelocityIn(shipOwner, station);

        velocity.Y.Should().BeLessThan(-5, "the ship has fallen for 32 steps, about 0.53 s");
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
        using World world = NewStationWorld(numericFrames, out FaultInjectingBackend faults);
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
    /// ship, which hangs in the ship's falling frame, exactly while their bubbles overlap. The ship's falling frame keeps
    /// no ghost of the hull: an anchor at rest joins the group of a fast body that the merge test links to it, so within
    /// that reach the ship's hull joins the system of space instead (ter-dhkf). Before, the falling frame kept a ghost of
    /// the station's hull while the ship's hull passed it. The station holds no crate here: the hull's ghost in the station
    /// would push a crate in its lane out of the station ahead of the hull, and the hull would join space for the crate too,
    /// through the place of the crate's ghost (ter-4psc).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AParkedStationsHullHasAGhostInAFastShipExactlyWhileTheirBubblesOverlapAndNoneInTheShipsFallingFrame(bool numericFrames)
    {
        using World world = NewStationWorld(numericFrames, out FaultInjectingBackend faults);
        StationScene scene = CratesPassingAStation(world, lane: 10, stationCrate: null, shipCrate: null);
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames);

        int overlapping = 0, falling = 0, inSpace = 0;
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
            if (overlap && home != scene.Space)
                falling++;
            if (overlap && home == scene.Space)
                inSpace++;
        }

        overlapping.Should().BeInRange(130, 145, "the bubbles overlap along 138 m of the ship's path");
        ghostInTheFallingFrame.Should().BeFalse("the station's hull, an anchor at rest, never has a ghost in the ship's falling frame");
        if (numericFrames)
        {
            falling.Should().BeGreaterThan(90, "the ship falls past the station while the bubbles overlap, beyond the reach of the merge test");
            inSpace.Should().BeInRange(25, 45, "within 19 m of the station's hull, the ship's hull joins its system, along 32 m of its path");
        }
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
        using World world = NewStationWorld(numericFrames, out _);
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
        using World world = NewStationWorld(numericFrames, out _);
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
        using World world = NewStationWorld(numericFrames, out _);
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
        using World world = NewStationWorld(numericFrames: true, out FaultInjectingBackend faults);
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
    /// Physics keeps a ghost only in the frames where the ghost pass can put one. A Kepler hull flies at 2 km/s in an orbit
    /// frame of its own past a static rock 8 m to the side of its path (see <see cref="Scenes.FastKeplerHull"/>), so the
    /// group that links them lives in the rock's home, the system of space, which keeps a ghost of the hull while the
    /// numeric update names it: while the merge test links the two, which reaches their bounding spheres plus 2 s of their
    /// relative speed, about 4 km. The sync point whose update stops naming the ghost drops it, and the next step does not
    /// run with it. A later reconcile allows none there either: space is the hull's semantic frame, and no other path of
    /// the ghost pass leads into its system. Before ter-dhkf rule 3, a falling ship beside a still Kepler station kept the
    /// ghost of the station's hull, and the ship now joins the hull's system instead.
    /// </summary>
    [Fact]
    public void TheSyncPointDropsTheGhostOfAnAnchorThatTheNumericUpdateNoLongerNames()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity hull = Scenes.FastKeplerHull(world, space, ShipHull);
        world.Spawn(new SpawnDesc { Frame = space, Pose = At(5_008, 0, -100), Body = new BodyDesc(new SphereShapeDesc(1), BodyMotion.Static) });
        FrameHandle buoy = EmptyFrame(world, space, At(-5000), 5);
        FrameHandle depot = EmptyFrame(world, space, At(-6000), 50);
        Tick(world, 2);
        NamesAnchorGhost(world, hull, space).Should().BeTrue("the hull starts 100 m from the rock");
        faults.Jolt.HasGhostIn(hull, space).Should().BeTrue();

        TickUntil(world, () => !NamesAnchorGhost(world, hull, space), 300);
        NamesAnchorGhost(world, hull, space).Should().BeFalse("the hull flies on until the merge test lets go of the rock");
        faults.Jolt.HasGhostIn(hull, space).Should().BeFalse("the update withdrew the ghost's place at this sync point");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();

        // A change far away makes the backend reconcile.
        world.Frames.Reparent(buoy, depot);
        world.Frames.Update();
        faults.Jolt.ApplyChanges(new PhysicsChanges(), world.Frames);

        faults.Jolt.HasGhostIn(hull, space).Should().BeFalse("no path of the ghost pass leads into the system of the hull's semantic frame");
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoSystemHoldsTwoBodiesOfOneEntityWhileCratesThatStickOutPassEachOther(bool numericFrames)
    {
        using World world = NewStationWorld(numericFrames, out FaultInjectingBackend faults);
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
    /// frame, but each stays at least 5 m away from the other bubble. Each crate has its one ghost outside its frame in the
    /// frame's tree parent, the home of the frame's hull: the station's crate in space, the ship's crate in the home of the
    /// ship's hull. The ship's hull, a dynamic body of space, may touch the ghost of the station's crate while it passes
    /// it, so the merge test links the hull to the crate's place, and the hull joins the system of space, the home of the
    /// station's hull, where that ghost lives (ticket ter-4psc). Meanwhile the ship hangs in space. No numeric frame keeps
    /// a ghost of the station's crate. The lookahead is 0.8 s: the merge test then reaches 55 m between the two hulls,
    /// short of the lane, and 53 m from the ship's hull to the place of the station's crate, 35 m from the lane, so the hull
    /// joins space about 40 m before it passes the crate. It leaves space beyond the split margin of 10 m more, about 53 m
    /// after it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BodiesThatStayAwayFromABubbleHaveNoGhostThereButOneInTheirFramesTreeParent(bool numericFrames)
    {
        using World world = NewStationWorld(numericFrames, out FaultInjectingBackend faults, lookahead: 0.8);
        StationScene scene = CratesPassingAStation(world, lane: 75, stationCrate: At(0, 40), shipCrate: At(0, -30));
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames);

        double closest = double.PositiveInfinity;
        int hullInSpace = 0;
        for (int tick = 0; tick < 200; tick++)
        {
            FrameHandle shipParent = world.Frames.TreeParent(scene.Ship);
            world.Tick();
            closest = Math.Min(closest, ShipDistance(world, scene));
            faults.Jolt.HasGhostIn(scene.StationOwner, scene.Ship).Should().BeFalse($"after tick {tick} the ship's bubble is beyond the reach of the station's hull");
            faults.Jolt.HasGhostIn(scene.ShipCrate, scene.Station).Should().BeFalse($"after tick {tick} the ship's crate is away from the station's bubble");
            faults.Jolt.HasGhostIn(scene.Crate, scene.Ship).Should().BeFalse($"after tick {tick} the station's crate is away from the ship's bubble");
            faults.Jolt.GhostCount(scene.ShipCrate).Should().BeLessThanOrEqualTo(1, $"after tick {tick} the ship's crate has no ghost but the one in the ship's tree parent");
            // The sync point that moves the ship's hull drops the crate's ghost in the old tree parent, and the ghost pass of
            // the next step makes the one in the new tree parent.
            if (world.Frames.TreeParent(scene.Ship) == shipParent)
                faults.Jolt.HasGhostIn(scene.ShipCrate, shipParent).Should().BeTrue($"after tick {tick} the ship's crate sticks out of the ship into its tree parent");
            faults.Jolt.HasGhostIn(scene.Crate, scene.Space).Should().BeTrue("the station's crate sticks out of the station into space");
            faults.Jolt.GhostCount(scene.Crate).Should().Be(1, $"after tick {tick} the station's crate has no ghost but the one in space");
            if (world.PhysicsHomeOf(scene.ShipOwner) == scene.Space)
                hullInSpace++;
        }

        hullInSpace.Should().BeInRange(numericFrames ? 88 : 200, numericFrames ? 98 : 200, numericFrames
            ? "the ship's hull lives in space while the merge test links it to the place of the station crate's ghost, along about 93 m of its path, and falls before and after"
            : "without numeric frames every hull lives in space");
        closest.Should().BeInRange(75, 75.01, "the ship passed the station");
        world.VelocityIn(scene.Crate, scene.Station).Length.Should().BeLessThan(1e-3, "nothing touched the station's crate");
        world.VelocityIn(scene.ShipCrate, scene.Ship).Length.Should().BeLessThan(1e-3, "nothing touched the ship's crate");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ghost follows the chord of its real body, as
    /// <see cref="TheGhostOfAFallingShipMovesInAParkedStationWithTheShipsVelocityOverTheLastStep"/> shows for a hull. In
    /// uniform gravity of 9.81 m/s² along -Y, the ship falls past the station from (1040, 30, 0), where it is after the
    /// warm-up, less what it fell. A crate at (-30, 0, 0) in the ship sticks out of the ship's bubble and into the
    /// station's, and a crate at (40, 0, 0) in the station sticks out of the station's bubble and into the ship's. The test
    /// reads the ghosts 3 ticks after the warm-up, when the station's crate has fallen about 1.4 m and its corners still
    /// stick out of the station's bubble. The station's crate stays at least 30 m from the ship's hull, beyond the 27 m at
    /// which the merge test links the hull to the place of the crate's ghost in space, so the hull falls in its numeric
    /// frame (ticket ter-4psc). The ship's falling frame and its hull accelerate, so their instant velocities at the end of a
    /// step are half a step of gravity, about 0.08 m/s, faster than their chords.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheGhostsOfCratesThatStickOutBesideAFallingShipMoveWithTheirVelocitiesOverTheLastStep(bool numericFrames)
    {
        using World world = NewStationWorld(numericFrames, out FaultInjectingBackend faults);
        StationScene scene = CratesPassingAStation(world, lane: 30, stationCrate: At(40), shipCrate: At(-30), shipX: 1040,
            gravity: GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Tick(world, 3);
        ShipHomeShouldBe(world, scene, numericFrames);

        JoltPhysicsBackend.BodyFacts shipCratesGhost = GhostOf(faults, scene.ShipCrate, scene.Station);
        JoltPhysicsBackend.BodyFacts stationCratesGhost = GhostOf(faults, scene.Crate, scene.Ship);
        Double3 shipCratesVelocity = world.VelocityIn(scene.ShipCrate, scene.Station);
        Double3 stationCratesVelocity = world.VelocityIn(scene.Crate, scene.Ship);

        shipCratesVelocity.Y.Should().BeLessThan(-5, "the ship and its crate have fallen for 32 steps, about 0.53 s");
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
    /// flies past at 60 m/s along -X with a gentle thruster: a hull that physics drives, with a bubble of radius 60 m. The
    /// scene returns after the <see cref="WarmUpTicks"/>, with the fast ship at (150, 140, 0). Neither ship fits in the
    /// other. Their bubbles overlap while their centers are at most 160 m apart, but the merge test, which reaches the
    /// hulls' radii plus 2 s of their relative speed, about 127 m, never links them, so the slow ship stays in the system
    /// of space when the fast one falls in a numeric frame of its own.
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
        Entity fastOwner = SpawnFastShip(world, space, new Double3(150, 140, 0), 60);
        Tick(world, WarmUpTicks);
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

    // ---- A body that sticks out of a ship in a numeric frame (ticket ter-4psc) ----
    //
    // A body that sticks out of a ship whose tree parent is a numeric frame is partly in the ship's semantic parent, as it
    // is without numeric frames. Its one ghost outside the ship lives in the ship's tree parent, the home of the ship's
    // hull. The merge test links each body of the semantic parent that may touch the ghost to the ghost's place, which no
    // body can move, so that body joins the hull's group and meets the ghost in the hull's home. The system of the semantic
    // parent keeps no ghost of the body. A ghost is kinematic, so only a dynamic body collides with it: the engine never
    // asks Jolt for contacts between a kinematic body and a static one, with numeric frames or without. So the rocks of
    // these scenes are dynamic balls.

    /// <summary>The radius of the big ship's bubble, in meters.</summary>
    private const float BigShipRadius = 200;

    /// <summary>A rock: a ball of radius 0.5 m and 1 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc RockBody = new(new SphereShapeDesc(0.5f), Mass: 1, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>The rock scene.</summary>
    /// <param name="Rock">The rock in space, or null when the scene has none.</param>
    private sealed record RockScene(FrameHandle Space, Entity ShipOwner, FrameHandle Ship, Entity Crate, Entity Rock);

    /// <summary>
    /// Space, with the field <paramref name="gravity"/> or none, and in it a ship: a hull of 4 m by 4 m by 4 m and 1000 kg
    /// that physics drives, at <paramref name="shipPose"/> with the velocity <paramref name="shipVelocity"/> and the
    /// components <paramref name="shipComponents"/>, or none, with a bubble of radius 200 m. A crate of 2 m by 2 m by 2 m
    /// and 2 kg floats at <paramref name="cratePose"/> in the ship. A rock (see <see cref="RockBody"/>) flies at
    /// <paramref name="rockVelocity"/> from <paramref name="rockPose"/> in space, or the scene has none.
    /// </summary>
    private static RockScene ShipAndRock(World world, DPose shipPose, Double3 shipVelocity, DPose cratePose, DPose? rockPose,
        Double3 rockVelocity = default, GravityField gravity = default, IReadOnlyList<IComponent>? shipComponents = null)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static, gravity));
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = shipPose,
            LinearVelocity = shipVelocity,
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(BigShipRadius), MotionDriver.Physics),
            Components = shipComponents ?? [],
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = cratePose, Body = CrateBody });
        Entity rock = rockPose is { } pose
            ? world.Spawn(new SpawnDesc { Frame = space, Pose = pose, LinearVelocity = rockVelocity, Body = RockBody })
            : Entity.Null;
        return new RockScene(space, shipOwner, ship, crate, rock);
    }

    /// <summary>
    /// The ship flies at 60 m/s along -X in space with a gentle thruster, so from its 30th tick of thrust its hull falls in
    /// a numeric frame of its own. The scene runs the <see cref="WarmUpTicks"/> before it returns, and the ship is then at
    /// (1100, 0, 0), less what it fell in the field <paramref name="gravity"/>. The crate floats at (0, -200, 0) in the
    /// ship, on the edge of its bubble, and so sticks out of it, or at (0, <paramref name="crateY"/>, 0). The rock rests at
    /// (1000, -200.6, 0) in space, in the path of the part of the crate that sticks out, 0.1 m outside the ship's bubble, or
    /// the scene has none. The merge test reaches the bounding spheres of the hull and the rock, plus 2 s of their relative
    /// speed, plus a margin of 10 m, about 134 m in all. The rock stays at least 200 m from the hull, so the merge test
    /// never links the rock to the hull itself. It links the rock to the place of the crate's ghost, which it reaches up to
    /// the bounding spheres of the crate and the rock plus 2 s of their relative speed, about 122 m: the crate is 99 m from
    /// the rock after the first tick after the warm-up, so from that tick's sync point on the rock lives in the home of the
    /// ship's hull, the falling frame where the crate's ghost lives.
    /// </summary>
    private static RockScene FallingShipPassingARock(World world, bool withRock = true, double crateY = -BigShipRadius,
        GravityField gravity = default)
    {
        RockScene scene = ShipAndRock(world, At(1100 + WarmUpDistance), new Double3(-ShipSpeed, 0, 0), At(0, crateY),
            withRock ? At(1000, -BigShipRadius - 0.6) : null, gravity: gravity, shipComponents: [ShipThruster()]);
        Tick(world, WarmUpTicks);
        return scene;
    }

    /// <summary>
    /// The ship rests at <see cref="RestDistance"/> + 150 m along +X in space, about 8,389 km from its origin and 150 m
    /// beyond the distance from which a slow group gets a resting frame, so its hull rests in a numeric frame of its own.
    /// The crate floats at (-200, 0, 0) in the ship, on the edge of its bubble, and so sticks out of it, or at
    /// (<paramref name="crateX"/>, 0, 0). The rock flies at 10 m/s along +X from <see cref="RestDistance"/> - 70 m in space
    /// at the crate, or the scene has none. The rock is nearer than that distance to the origin of space and slow, so alone
    /// it would need no numeric frame. The merge test reaches the bounding spheres of the hull and the rock plus 2 s of their
    /// relative speed, about 24 m, so it never links the rock to the hull itself, 200 m away. It links the rock to the place
    /// of the crate's ghost, 20 m away, which it reaches up to the bounding spheres of the crate and the rock plus 2 s of
    /// their relative speed, about 22 m, so from the first sync point on the rock rests in the home of the ship's hull, the
    /// resting frame where the crate's ghost lives. The face of the crate that points at the rock is 1 m outside the bubble,
    /// so the rock meets it before the rock touches the bubble.
    /// </summary>
    private static RockScene ShipRestingBesideARock(World world, bool withRock = true, double crateX = -BigShipRadius) =>
        ShipAndRock(world, At(RestDistance + 150), Double3.Zero, At(crateX), withRock ? At(RestDistance - 70) : null,
            rockVelocity: new Double3(10, 0, 0));

    /// <summary>
    /// Checks that the ship's hull is in a numeric frame of <paramref name="mode"/> with numeric frames, and in the system of
    /// space without.
    /// </summary>
    private static void ShipHomeShouldBe(World world, RockScene scene, bool numericFrames, NumericMode mode)
    {
        FrameHandle home = world.PhysicsHomeOf(scene.ShipOwner);
        if (numericFrames)
        {
            world.Frames.Numeric(home).Should().Be(mode,
                "a falling ship's hull thrusts at 60 m/s, and a resting ship lies beyond 2^23 m from the origin of space");
            world.Frames.TreeParent(home).Should().Be(scene.Space);
            world.Frames.TreeParent(scene.Ship).Should().Be(home, "the ship's frame hangs under the numeric frame of its hull");
        }
        else
        {
            home.Should().Be(scene.Space);
        }
    }

    /// <summary>What a run of <see cref="RunTheRock"/> saw.</summary>
    /// <param name="Ticks">The ticks after the first one until the contact with the crate's ghost had changed the rock's
    /// velocity as the test asks.</param>
    /// <param name="Position">The rock's position in space at the end of the run.</param>
    /// <param name="Velocity">The rock's velocity in space at the end of the run.</param>
    /// <param name="InSpace">True when the rock's semantic frame is still space at the end of the run.</param>
    /// <param name="HitInHullsHome">True when the step of the tick that changed the rock's velocity ran with the rock in the
    /// home of the ship's hull, where the crate's ghost lives.</param>
    /// <param name="HitMode">The mode of the rock's home in that step.</param>
    private sealed record RockRun(int Ticks, Double3 Position, Double3 Velocity, bool InSpace, bool HitInHullsHome, NumericMode HitMode);

    /// <summary>
    /// Runs the rock scene of <paramref name="build"/>, whose ship's hull rests or falls as <paramref name="mode"/> says,
    /// for 160 ticks after the first one, and records after how many of them <paramref name="done"/> first holds for the
    /// rock's velocity relative to space. So two runs end at the same tick, also when the contact comes one tick apart.
    /// With numeric frames, it checks after the first tick that the rock lives in the home of the ship's hull, because the
    /// merge test links it to the place of the crate's ghost. The step of a tick runs with each body in the home that the
    /// sync point of the tick before it gave the body, so the run reads the homes before each tick.
    /// </summary>
    private static RockRun RunTheRock(bool numericFrames, Func<World, RockScene> build, NumericMode mode, Func<Double3, bool> done)
    {
        const int total = 160;
        using World world = NewWorld(numericFrames, out _);
        RockScene scene = build(world);
        world.Tick();
        ShipHomeShouldBe(world, scene, numericFrames, mode);
        world.PhysicsHomeOf(scene.Rock).Should().Be(world.PhysicsHomeOf(scene.ShipOwner),
            "the merge test links the rock to the place of the crate's ghost, which lives in the home of the ship's hull");

        FrameHandle rockHome = FrameHandle.Null, hullHome = FrameHandle.Null;
        int ticks = TickUntil(world, () =>
        {
            if (done(world.VelocityIn(scene.Rock, scene.Space)))
                return true;
            rockHome = world.PhysicsHomeOf(scene.Rock);
            hullHome = world.PhysicsHomeOf(scene.ShipOwner);
            return false;
        }, total);
        Tick(world, total - ticks);

        world.Errors.Should().BeEmpty();
        return new RockRun(ticks, world.PoseIn(scene.Rock, scene.Space).Position, world.VelocityIn(scene.Rock, scene.Space),
            world.GetFrame(scene.Rock) == scene.Space, rockHome == hullHome, world.Frames.Numeric(rockHome));
    }

    /// <summary>
    /// The rock meets the crate's ghost in the ship's falling frame, where the rock moves at 60 m/s toward a ghost at rest,
    /// while without numeric frames the ghost moves at 60 m/s toward the rock at rest in space. Without numeric frames, Jolt
    /// pushes the rock only in the step after the crate's face reaches it, when the face has gone 0.5 m into the rock; in
    /// the falling frame it stops the rock at the face in the step in which the face reaches it. So the push may come one
    /// tick earlier with numeric frames, and the runs compare the rock 160 ticks after their first, when Jolt has pushed
    /// the rock out of the crate's face in both.
    /// </summary>
    [Fact]
    public void ACrateThatSticksOutOfAFallingShipPushesARockBeyondTheReachOfTheHullInTheShipsFallingFrameAsItDoesWithoutNumericFrames()
    {
        Func<World, RockScene> build = world => FallingShipPassingARock(world);
        RockRun withNumericFrames = RunTheRock(numericFrames: true, build, NumericMode.Falling, velocity => velocity.Length > 10);
        RockRun withoutNumericFrames = RunTheRock(numericFrames: false, build, NumericMode.None, velocity => velocity.Length > 10);

        // The front of the crate reaches the back of the rock, at x = 1000.5 in space, after 98.5 m, or 1.64 s.
        withoutNumericFrames.Ticks.Should().BeInRange(96, 102, "the ghost of the crate in space reaches the rock after about 1.64 s");
        withoutNumericFrames.Velocity.X.Should().BeLessThan(-10, "the ghost pushed the rock along -X");
        withoutNumericFrames.InSpace.Should().BeTrue("the rock never touched the ship's bubble");
        withNumericFrames.HitInHullsHome.Should().BeTrue(
            "the merge test linked the rock to the place of the crate's ghost, so the rock met the ghost in the ship's falling frame");
        withNumericFrames.HitMode.Should().Be(NumericMode.Falling);
        withNumericFrames.Ticks.Should().BeInRange(withoutNumericFrames.Ticks - 1, withoutNumericFrames.Ticks + 1,
            "the crate's ghost in the ship's tree parent is at the same place");
        (withNumericFrames.Position - withoutNumericFrames.Position).Length.Should().BeLessThan(1e-3, "the ghost pushed the rock to the same place");
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2, "the ghost moves at the same velocity");
        withNumericFrames.InSpace.Should().BeTrue();
    }

    [Fact]
    public void ACrateThatSticksOutOfAShipThatRestsFarOutStopsARockBeyondTheReachOfTheHullInTheShipsRestingFrameAsItDoesWithoutNumericFrames()
    {
        Func<World, RockScene> build = world => ShipRestingBesideARock(world);
        RockRun withNumericFrames = RunTheRock(numericFrames: true, build, NumericMode.Resting, velocity => velocity.Length < 1);
        RockRun withoutNumericFrames = RunTheRock(numericFrames: false, build, NumericMode.None, velocity => velocity.Length < 1);

        // The front of the rock reaches the face of the crate, at x = RestDistance - 51 in space, after 18.5 m, or 1.85 s.
        withoutNumericFrames.Ticks.Should().BeInRange(108, 114, "the rock reaches the ghost of the crate in space after about 1.85 s");
        withoutNumericFrames.Position.X.Should().BeLessThan(RestDistance - 51.4, "the rock stopped at the ghost, which did not let it through");
        withoutNumericFrames.InSpace.Should().BeTrue("the rock stopped before it touched the ship's bubble");
        withNumericFrames.HitInHullsHome.Should().BeTrue(
            "the merge test linked the rock to the place of the crate's ghost, so the rock met the ghost in the ship's resting frame");
        withNumericFrames.HitMode.Should().Be(NumericMode.Resting);
        withNumericFrames.Ticks.Should().Be(withoutNumericFrames.Ticks, "the crate's ghost in the ship's tree parent is at the same place");
        (withNumericFrames.Position - withoutNumericFrames.Position).Length.Should().BeLessThan(1e-3, "the ghost stopped the rock at the same place");
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2);
        withNumericFrames.InSpace.Should().BeTrue();
    }

    /// <summary>
    /// The ship rests at <see cref="RestDistance"/> + 150 m along +X in space, beyond the distance from which a slow group
    /// gets a resting frame, so its hull rests in a numeric frame, under which the ship's frame hangs. The crate floats at
    /// (-200, 0, 0) in the ship, on the edge of its bubble, and drifts outward along -X at 2 m/s. A rock (a ball of radius
    /// 0.5 m and 1 kg, slowed by its damping) rests in space 0.1 m in front of the crate's face, outside the bubble. The
    /// merge test links the rock to the place of the crate's ghost, so from the first sync point the rock rests in the
    /// hull's resting frame, the ship's tree parent, where the crate's ghost lives (ticket ter-4psc). The ghost reaches the
    /// rock at once and pushes it ahead. When the crate is fully outside the bubble plus the exit margin, it leaves the ship
    /// into the ship's tree parent while it still pushes the rock: into space without numeric frames, into the resting
    /// frame with them. Either way the rock's body is in that system, and the crate's body there takes over the contact of
    /// its ghost. In both runs, each entity hears one Collision and no end (review of ter-ejx9).
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
            Pose = At(RestDistance + 150),
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
        // The crate's face is at x = RestDistance - 51 in space, and the rock's side at x = RestDistance - 51.1.
        Entity rock = world.Spawn(new SpawnDesc
        {
            Frame = space, Pose = At(RestDistance - 51.6), Body = RockBody with { LinearDamping = 0.5f }, Components = [rockLog],
        });

        world.Tick();
        if (numericFrames)
            world.PhysicsHomeOf(rock).Should().Be(world.PhysicsHomeOf(shipOwner),
                "the merge test links the rock to the place of the crate's ghost, which lives in the hull's resting frame");
        int handOff = 1 + TickUntil(world, () => world.GetFrame(crate) == space, 400);

        world.GetFrame(crate).Should().Be(space, "the crate left the ship's bubble");
        if (numericFrames)
        {
            world.Frames.Numeric(world.PhysicsHomeOf(crate)).Should().Be(NumericMode.Resting, "the crate left into the ship's tree parent");
            world.PhysicsHomeOf(crate).Should().Be(world.PhysicsHomeOf(rock), "the rock rests in the ship's tree parent");
            faults.Outputs[^1].HandOffs.Should().Contain(h => h.Entity == crate && world.Frames.IsNumeric(h.To));
            // The crate's body has replaced its ghost, and the ledger still counts the contact until the next step finds it
            // between the two bodies.
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
    /// The crate's one ghost outside the ship lives in the ship's tree parent, the numeric frame of its hull, and neither
    /// the ghost pass nor the reconcile puts one in the system of space, the ship's semantic parent.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheGhostOfACrateThatSticksOutOfAShipInANumericFrameSurvivesAReconcileInTheShipsTreeParentAndNoneIsInSpace(bool shipRests)
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        RockScene scene = shipRests ? ShipRestingBesideARock(world, withRock: false) : FallingShipPassingARock(world, withRock: false);
        FrameHandle buoy = EmptyFrame(world, scene.Space, At(-5000), 5);
        FrameHandle depot = EmptyFrame(world, scene.Space, At(5000), 50);
        // The sync point of the first tick after the warm-up gives the falling ship's hull its numeric frame, after that
        // tick's step, and the ghost pass of the next step puts the crate's ghost there.
        Tick(world, 2);
        ShipHomeShouldBe(world, scene, numericFrames: true, shipRests ? NumericMode.Resting : NumericMode.Falling);
        FrameHandle home = world.PhysicsHomeOf(scene.ShipOwner);
        faults.Jolt.HasGhostIn(scene.Crate, home).Should().BeTrue("the crate sticks out of the ship into its tree parent");
        faults.Jolt.HasGhostIn(scene.Crate, scene.Space).Should().BeFalse("no body of space may touch the crate's ghost");

        // A change far away makes the backend reconcile. The ship is still a semantic child of space.
        world.Frames.Reparent(buoy, depot);
        world.Frames.Update();
        faults.Jolt.ApplyChanges(new PhysicsChanges(), world.Frames);

        faults.Jolt.HasGhostIn(scene.Crate, home).Should().BeTrue(
            "the crate sticks out of the ship, so it is partly in the ship's tree parent, the home of the ship's hull");
        faults.Jolt.HasGhostIn(scene.Crate, scene.Space).Should().BeFalse("the tree allows the crate no ghost in the ship's semantic parent");
        faults.Jolt.GhostCount(scene.Crate).Should().Be(1);
        world.Tick();
        faults.Jolt.HasGhostIn(scene.Crate, home).Should().BeTrue();
        faults.Jolt.GhostCount(scene.Crate).Should().Be(1);
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
    /// bubble, so the crate has its ghost in the ship's tree parent, the home of the ship's hull: the hull's falling frame
    /// with numeric frames, space without. The ship's falling frame accelerates, so its instant velocity at the end of a
    /// step is half a step of gravity, about 0.08 m/s, faster than its chord. The test reads the ghost 3 ticks after the
    /// warm-up, when the ship's hull has fallen in its numeric frame for 3 ticks, and compares it with the crate's place and
    /// velocity relative to the ghost's frame. The crate has no other ghost.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheGhostOfACrateThatSticksOutOfAFallingShipHasTheCratesPlaceAndItsVelocityOverTheLastStepInTheShipsTreeParent(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        RockScene scene = FallingShipPassingARock(world, withRock: false, gravity: GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Tick(world, 3);
        ShipHomeShouldBe(world, scene, numericFrames, NumericMode.Falling);

        FrameHandle treeParent = world.Frames.TreeParent(scene.Ship);
        JoltPhysicsBackend.BodyFacts ghost = GhostOf(faults, scene.Crate, treeParent);
        Double3 velocity = world.VelocityIn(scene.Crate, treeParent);

        world.VelocityIn(scene.Crate, scene.Space).Y.Should().BeLessThan(-5, "the ship and its crate have fallen for 32 steps, about 0.53 s");
        (ghost.Position - world.PoseIn(scene.Crate, treeParent).Position).Length.Should().BeLessThan(1e-6,
            "the ghost is where the crate is in the ship's tree parent");
        ((Double3)ghost.LinearVelocity - velocity).Length.Should().BeLessThan(1e-4, "the ghost moves in the ship's tree parent with the crate's chord");
        faults.Jolt.GhostCount(scene.Crate).Should().Be(1, "no body of space may touch the crate's ghost, so the system of space keeps none");
        world.Errors.Should().BeEmpty();
    }
}
