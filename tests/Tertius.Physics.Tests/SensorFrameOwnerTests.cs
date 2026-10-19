using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A sensor detects the same things of a frame owner in every frame (ticket ter-7ul9, item 3 of
/// docs/plans/complete-ghosts.md). The owner is a body of the hull's motion everywhere: the hull in the frame's tree parent,
/// the interior in the frame itself, which is kinematic and at rest while the hull moves and static while the hull is
/// static, and the hull's ghost in any other frame, which meets what the hull meets. So by Jolt's rule a sensor detects a
/// ship whose hull moves, and only a dynamic sensor detects a parked ship (fork F4). A change of the hull's motion ends each
/// pair that the new motion cannot make, and starts each pair that it can make. Each scene across frames has a twin with
/// the bodies in one frame: the sensor beside a plain body with the hull's shape and motion, or, for a change of motion,
/// which a plain body cannot make, a static sensor beside the real hull in the frame's tree parent (<see cref="Place.Parent"/>).
/// </summary>
public class SensorFrameOwnerTests
{
    /// <summary>Where the sensor of <see cref="ShipAndSensor"/> lives, and so which body of the ship's owner it meets.</summary>
    public enum Place
    {
        /// <summary>The ship's tree parent, beside the hull. Only a static sensor stays there: a body that moves would move into the ship's frame.</summary>
        Parent,

        /// <summary>The ship's own frame, on the interior.</summary>
        Frame,

        /// <summary>A sibling frame of the ship's frame, on the ghost of the hull.</summary>
        Sibling,
    }

    /// <summary>The half side of the ship's hull, a box, in meters. Its face toward +X lies at x = 2 m.</summary>
    private const float HullHalf = 2;

    /// <summary>The radius of the ship's bubble, in meters, larger than the 3.46 m of the hull's corners.</summary>
    private const float ShipRadius = 6;

    /// <summary>The radius of the sensor ball, in meters.</summary>
    private const float SensorRadius = 0.5f;

    /// <summary>Where the sensor's center lies along X in the ship's tree parent: the ball reaches 0.2 m into the hull's face.</summary>
    private const double SensorX = 2.3;

    /// <summary>
    /// The place and the radius of the sibling bubble along X in the ship's tree parent. Its edge lies at x = 1 m, inside the
    /// hull, so the hull's ghost sits in it, and the sensor's center lies 1.3 m inside it, more than the sensor's radius, so
    /// the sensor does not stick out. The ship's bubble does not fit in it, so a dynamic hull, which enters a bubble that it
    /// touches when its frame fits there, stays in the ship's tree parent.
    /// </summary>
    private const double SiblingX = 6;

    /// <inheritdoc cref="SiblingX"/>
    private const float SiblingRadius = 5;

    /// <summary>A box of <see cref="HullHalf"/> and 1000 kg with <paramref name="motion"/> that nothing slows down.</summary>
    private static BodyDesc Box(BodyMotion motion) =>
        new(new BoxShapeDesc(new Vector3(HullHalf)), motion, Mass: 1000, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A sensor ball of <see cref="SensorRadius"/> and 1 kg with <paramref name="motion"/> that nothing slows down.</summary>
    private static BodyDesc SensorBall(BodyMotion motion) =>
        new BodyDesc(new SphereShapeDesc(SensorRadius), motion, Mass: 1, LinearDamping: 0, AngularDamping: 0) { IsSensor = true };

    /// <summary>The scene of <see cref="ShipAndSensor"/>: the ship's owner and the sensor, with what each heard, and the sensor's home.</summary>
    private sealed record Scene(Entity Owner, Entity Sensor, CollisionLog OwnerLog, CollisionLog SensorLog, FrameHandle Home);

    /// <summary>
    /// A ship at the origin of space, with no gravity: a hull of <see cref="Box"/> with <paramref name="hull"/>, which holds
    /// still, and a bubble of <see cref="ShipRadius"/>. A sensor ball with <paramref name="sensor"/> at rest, in
    /// <paramref name="place"/>, at x = <see cref="SensorX"/> in space, overlaps the hull, the interior or the hull's ghost
    /// there by 0.2 m. A kinematic hull has no rails component, so it holds still too.
    /// </summary>
    private static Scene ShipAndSensor(World world, BodyMotion hull, BodyMotion sensor, Place place)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var ownerLog = new CollisionLog();
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space, Body = Box(hull), AsFrame = new FrameSpec(FrameBounds.Sphere(ShipRadius)), Components = [ownerLog],
        });
        (FrameHandle home, double x) = place switch
        {
            Place.Parent => (space, SensorX),
            Place.Frame => (world.GetOwnedFrame(owner), SensorX),
            _ => (Bubble(world, space, SiblingX, SiblingRadius), SensorX - SiblingX),
        };
        var sensorLog = new CollisionLog();
        Entity sensorEntity = world.Spawn(new SpawnDesc { Frame = home, Pose = At(x), Body = SensorBall(sensor), Components = [sensorLog] });
        return new Scene(owner, sensorEntity, ownerLog, sensorLog, home);
    }

    /// <summary>A frame without a body and with no gravity, with a bubble of <paramref name="radius"/> m at x = <paramref name="x"/> in <paramref name="parent"/>.</summary>
    private static FrameHandle Bubble(World world, FrameHandle parent, double x, float radius) =>
        world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = parent, Pose = At(x), AsFrame = new FrameSpec(FrameBounds.Sphere(radius)) }));

    /// <summary>
    /// The table of case 3: a sensor detects the owner as a body of the hull's motion, by Jolt's rule. A dynamic sensor
    /// detects every body, and a static or kinematic sensor detects a body that moves.
    /// </summary>
    private static bool Detects(BodyMotion sensor, BodyMotion hull) => sensor == BodyMotion.Dynamic || hull != BodyMotion.Static;

    /// <summary>Checks that the backend keeps its invariants, the contact ledger among them, and that the world recorded no error.</summary>
    private static void AllIsWell(World world, FaultInjectingBackend faults)
    {
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }

    // ---- What a sensor detects of a frame owner ----

    public static TheoryData<BodyMotion, BodyMotion, Place> EveryPlaceOfASensor()
    {
        var data = new TheoryData<BodyMotion, BodyMotion, Place>();
        foreach (BodyMotion hull in new[] { BodyMotion.Static, BodyMotion.Kinematic, BodyMotion.Dynamic })
        {
            data.Add(BodyMotion.Static, hull, Place.Parent);
            foreach (BodyMotion sensor in new[] { BodyMotion.Static, BodyMotion.Kinematic, BodyMotion.Dynamic })
            {
                data.Add(sensor, hull, Place.Frame);
                data.Add(sensor, hull, Place.Sibling);
            }
        }
        return data;
    }

    /// <summary>
    /// The table of case 3 in every place of the sensor: on the hull in the ship's tree parent, on the interior in the ship's
    /// frame, and on the hull's ghost in a sibling frame. Only a static sensor stays in the tree parent. Its twin in one
    /// frame is <see cref="ASensorDetectsAPlainBodyOfTheHullsShapeWhenTheSensorIsDynamicOrTheBodyMoves"/>. Before ter-7ul9,
    /// a static or a kinematic sensor never detected the interior, which was always static.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPlaceOfASensor))]
    public void ASensorDetectsAShipInEveryFrameWhenTheSensorIsDynamicOrTheShipsHullMoves(BodyMotion sensor, BodyMotion hull, Place place)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scene scene = ShipAndSensor(world, hull, sensor, place);
        Tick(world, 10);

        world.GetFrame(scene.Sensor).Should().Be(scene.Home);
        if (place == Place.Sibling)
            faults.Jolt.HasGhostIn(scene.Owner, scene.Home).Should().BeTrue("the hull reaches the sibling's bubble");
        List<ContactNews> expected = Detects(sensor, hull) ? [Started] : [];
        scene.SensorLog.NewsOf(scene.Owner).Should().Equal(expected);
        scene.OwnerLog.NewsOf(scene.Sensor).Should().Equal(expected);
        AllIsWell(world, faults);
    }

    public static TheoryData<BodyMotion, BodyMotion> EveryPairOfMotions()
    {
        var data = new TheoryData<BodyMotion, BodyMotion>();
        foreach (BodyMotion body in new[] { BodyMotion.Static, BodyMotion.Kinematic, BodyMotion.Dynamic })
            foreach (BodyMotion sensor in new[] { BodyMotion.Static, BodyMotion.Kinematic, BodyMotion.Dynamic })
                data.Add(sensor, body);
        return data;
    }

    /// <summary>
    /// The twin in one frame of <see cref="ASensorDetectsAShipInEveryFrameWhenTheSensorIsDynamicOrTheShipsHullMoves"/>: the
    /// sensor at the same place beside a plain box of the hull's shape and motion in space detects it by the same table.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPairOfMotions))]
    public void ASensorDetectsAPlainBodyOfTheHullsShapeWhenTheSensorIsDynamicOrTheBodyMoves(BodyMotion sensor, BodyMotion body)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var boxLog = new CollisionLog();
        Entity box = world.Spawn(new SpawnDesc { Frame = space, Body = Box(body), Components = [boxLog] });
        var sensorLog = new CollisionLog();
        Entity sensorEntity = world.Spawn(new SpawnDesc { Frame = space, Pose = At(SensorX), Body = SensorBall(sensor), Components = [sensorLog] });
        Tick(world, 10);

        List<ContactNews> expected = Detects(sensor, body) ? [Started] : [];
        sensorLog.NewsOf(box).Should().Equal(expected);
        boxLog.NewsOf(sensorEntity).Should().Equal(expected);
        AllIsWell(world, faults);
    }

    // ---- A change of the hull's motion ----

    /// <summary>
    /// A static sensor detects a ship whose hull moves, and the ship parks. The parked ship is a static body in every frame,
    /// which a static sensor does not detect, so the sensor hears the end of the touch, also on the hull in the ship's tree
    /// parent, the twin in one frame, where Jolt simulates neither the parked hull nor the sensor and so never drops the
    /// pair on its own. Before ter-7ul9 the touch went on in the tree parent and in the sibling, and the sensor in the frame
    /// never detected the static interior.
    /// </summary>
    [Theory]
    [InlineData(Place.Parent, BodyMotion.Kinematic)]
    [InlineData(Place.Parent, BodyMotion.Dynamic)]
    [InlineData(Place.Frame, BodyMotion.Kinematic)]
    [InlineData(Place.Frame, BodyMotion.Dynamic)]
    [InlineData(Place.Sibling, BodyMotion.Kinematic)]
    [InlineData(Place.Sibling, BodyMotion.Dynamic)]
    public void AStaticSensorStopsDetectingAShipWhenTheShipParks(Place place, BodyMotion from)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scene scene = ShipAndSensor(world, from, BodyMotion.Static, place);
        Tick(world, 10);
        scene.SensorLog.NewsOf(scene.Owner).Should().Equal(Started);

        world.SetMotionNextTick(scene.Owner, BodyMotion.Static);
        Tick(world, 10);

        scene.SensorLog.NewsOf(scene.Owner).Should().Equal(Started, Ended);
        scene.OwnerLog.NewsOf(scene.Sensor).Should().Equal(Started, Ended);
        AllIsWell(world, faults);
    }

    /// <summary>
    /// A static sensor overlaps a parked ship, which it does not detect, and the ship starts to move. The moving ship is a
    /// body that a static sensor detects in every frame, so the touch starts, also on the hull in the ship's tree parent, the
    /// twin in one frame. Before ter-7ul9 the interior stayed static, and the filter had dropped the pair of the sensor and
    /// the hull's static ghost, which Jolt never offered again.
    /// </summary>
    [Theory]
    [InlineData(Place.Parent, BodyMotion.Kinematic)]
    [InlineData(Place.Parent, BodyMotion.Dynamic)]
    [InlineData(Place.Frame, BodyMotion.Kinematic)]
    [InlineData(Place.Frame, BodyMotion.Dynamic)]
    [InlineData(Place.Sibling, BodyMotion.Kinematic)]
    [InlineData(Place.Sibling, BodyMotion.Dynamic)]
    public void AStaticSensorStartsToDetectAParkedShipThatItOverlapsWhenTheShipStartsToMove(Place place, BodyMotion to)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scene scene = ShipAndSensor(world, BodyMotion.Static, BodyMotion.Static, place);
        Tick(world, 10);
        scene.SensorLog.NewsOf(scene.Owner).Should().BeEmpty();

        world.SetMotionNextTick(scene.Owner, to);
        Tick(world, 10);

        scene.SensorLog.NewsOf(scene.Owner).Should().Equal(Started);
        scene.OwnerLog.NewsOf(scene.Sensor).Should().Equal(Started);
        AllIsWell(world, faults);
    }

    /// <summary>
    /// The ship of <see cref="AStaticSensorStopsDetectingAShipWhenTheShipParks"/> parks and moves again. The sensor detects it
    /// again, so the end of the touch at the park left nothing behind that hides the new touch.
    /// </summary>
    [Theory]
    [InlineData(Place.Parent)]
    [InlineData(Place.Frame)]
    [InlineData(Place.Sibling)]
    public void AStaticSensorDetectsAShipAgainWhenTheShipMovesAgainAfterItParked(Place place)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scene scene = ShipAndSensor(world, BodyMotion.Kinematic, BodyMotion.Static, place);
        Tick(world, 10);
        world.SetMotionNextTick(scene.Owner, BodyMotion.Static);
        Tick(world, 10);

        world.SetMotionNextTick(scene.Owner, BodyMotion.Kinematic);
        Tick(world, 10);

        scene.SensorLog.NewsOf(scene.Owner).Should().Equal(Started, Ended, Started);
        AllIsWell(world, faults);
    }

    /// <summary>
    /// A dynamic sensor detects a body of every motion, so its touch of a ship goes on when the ship parks: on the interior
    /// in the ship's frame, which keeps its body, and on the hull's ghost in a sibling, which is made again, whose new ghost
    /// takes the touch over. Its twin in one frame is the dynamic sensor beside a plain static box and a plain kinematic or
    /// dynamic box in <see cref="ASensorDetectsAPlainBodyOfTheHullsShapeWhenTheSensorIsDynamicOrTheBodyMoves"/>.
    /// </summary>
    [Theory]
    [InlineData(Place.Frame, BodyMotion.Kinematic)]
    [InlineData(Place.Frame, BodyMotion.Dynamic)]
    [InlineData(Place.Sibling, BodyMotion.Kinematic)]
    [InlineData(Place.Sibling, BodyMotion.Dynamic)]
    public void ADynamicSensorKeepsDetectingAShipWhenTheShipParks(Place place, BodyMotion from)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scene scene = ShipAndSensor(world, from, BodyMotion.Dynamic, place);
        Tick(world, 10);

        world.SetMotionNextTick(scene.Owner, BodyMotion.Static);
        Tick(world, 10);

        scene.SensorLog.NewsOf(scene.Owner).Should().Equal(Started);
        scene.OwnerLog.NewsOf(scene.Sensor).Should().Equal(Started);
        AllIsWell(world, faults);
    }

    /// <summary>
    /// The invariants find an interior whose Jolt motion does not follow its hull's: the interior of a ship with a kinematic
    /// hull is kinematic, and the test turns it static in Jolt, as only a bug would.
    /// </summary>
    [Fact]
    public void TheInvariantsFindAnInteriorWhoseJoltMotionDoesNotFollowTheHulls()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scene scene = ShipAndSensor(world, BodyMotion.Kinematic, BodyMotion.Static, Place.Parent);
        world.Tick();
        FrameHandle ship = world.GetOwnedFrame(scene.Owner);
        JoltPhysicsBackend.BodyFacts interior = faults.Jolt.BodiesIn(ship).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Interior);
        interior.Motion.Should().Be(MotionType.Kinematic, "the hull moves");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();

        faults.Jolt.SystemOf(ship)!.BodyInterface.SetMotionType(interior.Id, MotionType.Static, Activation.DontActivate);

        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().Throw<ContractException>();
    }

    /// <summary>
    /// The interior of a root frame is always static, since a root frame has no hull. In the World a frame never becomes a
    /// root after its spawn, but the frame tree lets a frame become a root when its root parent dies, and physics then
    /// removes the frame's hull. A test stands in for that change: it moves the ship's frame out of space, while a static
    /// sensor in the ship's frame detects the interior of the ship, whose hull moves. The interior becomes static, as the
    /// interior of every root frame is, so the sensor's pair with it ends, as the pair of a static sensor and the interior
    /// of a ship that parks ends.
    /// </summary>
    [Theory]
    [InlineData(BodyMotion.Kinematic)]
    [InlineData(BodyMotion.Dynamic)]
    public void TheInteriorOfAFrameThatBecomesARootTurnsStaticAndAStaticSensorStopsDetectingIt(BodyMotion hull)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults, debugChecks: false);
        Scene scene = ShipAndSensor(world, hull, BodyMotion.Static, Place.Frame);
        Tick(world, 10);
        scene.SensorLog.NewsOf(scene.Owner).Should().Equal(Started);
        FrameHandle ship = world.GetOwnedFrame(scene.Owner);

        world.Frames.Reparent(ship, FrameHandle.Null);
        Tick(world, 10);

        faults.Jolt.BodiesIn(ship).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Interior).Motion
            .Should().Be(MotionType.Static, "the interior of a root frame is always static");
        scene.SensorLog.NewsOf(scene.Owner).Should().Equal(Started, Ended);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
    }

    /// <summary>
    /// A lander rests on a floor under a gravity of 9.81 m/s², and a crate of 10 kg rests on the top of the lander's
    /// interior, a box of 4 m at the origin of the lander's frame, where the frame's proper acceleration gives it its weight.
    /// The lander parks, which makes the interior static, and its engines start again, which makes the interior kinematic
    /// and at rest. A dynamic crate meets a body of each motion, so it hears no end of its touch of the interior. There is
    /// no twin in one frame: the crate and the interior are in one frame already.
    /// </summary>
    [Fact]
    public void ACrateThatRestsOnAShipsInteriorHearsNoEndWhenTheShipParksOrWhenItsEnginesStart()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = ground, Pose = At(0, HullHalf), Body = Box(BodyMotion.Dynamic), AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        FrameHandle lander = world.GetOwnedFrame(owner);
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc { Frame = lander, Pose = At(0, HullHalf + 0.5), Body = Cube(), Components = [crateLog] });
        Tick(world, 30);
        crateLog.NewsOf(owner).Should().Equal(Started);

        world.SetMotionNextTick(owner, BodyMotion.Static);
        Tick(world, 30);
        world.SetMotionNextTick(owner, BodyMotion.Dynamic);
        Tick(world, 30);

        world.GetFrame(crate).Should().Be(lander);
        world.PoseIn(crate, lander).Position.Y.Should().BeApproximately(HullHalf + 0.5, 0.05, "the crate rests on the interior");
        crateLog.NewsOf(owner).Should().Equal(Started);
        AllIsWell(world, faults);
    }
}
