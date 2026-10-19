using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A contact of two ghosts, one of them a sensor, is a contact of their entities (the first half of ticket ter-3mwi, item 2
/// of docs/plans/complete-ghosts.md). Jolt makes a contact of two ghosts only when one of them is a sensor, because no ghost
/// is dynamic in Jolt. The contact filter judges such a pair by the contact motions of both ghosts, the motions of their
/// real bodies, and the ledger counts it once, however many systems hold it. Each scene runs across frames and with the
/// same bodies in one frame, and both runs hear the same events.
/// </summary>
public class GhostPairTests
{
    private static FrameHandle Space(World world) => world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));

    /// <summary>A frame without a body in <paramref name="parent"/> at <paramref name="pose"/>, with no gravity and a bubble of <paramref name="radius"/> m.</summary>
    private static FrameHandle Bubble(World world, FrameHandle parent, DPose pose, float radius) =>
        world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = parent, Pose = pose, AsFrame = new FrameSpec(FrameBounds.Sphere(radius)) }));

    /// <summary>A dynamic sensor ball of radius 0.5 m and 1 kg that nothing slows down.</summary>
    private static readonly BodyDesc SensorBall = Ball() with { IsSensor = true };

    /// <summary>
    /// The scene of <see cref="SensorAndBody"/>. <paramref name="First"/> and <paramref name="Second"/> are the homes of the
    /// sensor and the body, which are <paramref name="Parent"/> in the run in one frame.
    /// </summary>
    private sealed record PairScene(FrameHandle Parent, FrameHandle First, FrameHandle Second, Entity Sensor, Entity Body,
        CollisionLog SensorLog, CollisionLog BodyLog, Pilot SensorPilot);

    /// <summary>
    /// Case 2 of the design. In <paramref name="parent"/>, with no gravity, two sibling bubbles of radius 3 m lie 1.4 m apart
    /// along X: the first ends at x = -0.7 m, the second begins at x = 0.7 m. A dynamic sensor ball of radius 0.5 m in the
    /// first frame, at x = -0.4 m in the parent, overlaps a dynamic body of <paramref name="body"/>, a ball of radius 0.5 m,
    /// in the second frame at x = 0.4 m, by 0.2 m. Each center is 0.3 m outside its own bubble, less than the 1 m at which it
    /// would leave, so each sticks out into the parent. Each center is 1.1 m from the other's bubble, beyond its radius, and a
    /// dynamic body reaches only the bubble of another frame (rule 2), so neither has a ghost in the other's frame. The
    /// parent's system holds the stick-out ghost of each, and the two overlap only there. Without
    /// <paramref name="acrossFrames"/>, there are no bubbles, and both are real bodies of the parent at the same places.
    /// The sensor's home is the first frame, at x = -3.7 m, so its pose there is 3.3 m along X.
    /// </summary>
    private static PairScene SensorAndBody(World world, FrameHandle parent, bool acrossFrames, BodyDesc body)
    {
        FrameHandle first = acrossFrames ? Bubble(world, parent, At(-3.7), 3) : parent;
        FrameHandle second = acrossFrames ? Bubble(world, parent, At(3.7), 3) : parent;
        double firstX = acrossFrames ? -3.7 : 0, secondX = acrossFrames ? 3.7 : 0;
        var sensorLog = new CollisionLog();
        var bodyLog = new CollisionLog();
        var pilot = new Pilot();
        Entity sensor = world.Spawn(new SpawnDesc { Frame = first, Pose = At(-0.4 - firstX), Body = SensorBall, Components = [sensorLog, pilot] });
        Entity other = world.Spawn(new SpawnDesc { Frame = second, Pose = At(0.4 - secondX), Body = body, Components = [bodyLog] });
        return new PairScene(parent, first, second, sensor, other, sensorLog, bodyLog, pilot);
    }

    /// <summary>Checks that only the parent's system holds a body of each entity of the scene across frames: their two ghosts.</summary>
    private static void OnlyTheParentHoldsBoth(FaultInjectingBackend faults, PairScene scene)
    {
        faults.Jolt.HasGhostIn(scene.Sensor, scene.Parent).Should().BeTrue("the sensor sticks out of its bubble");
        faults.Jolt.HasGhostIn(scene.Body, scene.Parent).Should().BeTrue("the body sticks out of its bubble");
        faults.Jolt.HasGhostIn(scene.Sensor, scene.Second).Should().BeFalse("a dynamic sensor reaches only the bubble of another frame");
        faults.Jolt.HasGhostIn(scene.Body, scene.First).Should().BeFalse("a dynamic body reaches only the bubble of another frame");
    }

    // ---- A sensor and a body that touch only through two ghosts ----

    /// <summary>
    /// In the scene of <see cref="SensorAndBody"/>, the sensor and the ball overlap from the start. Each hears one Collision,
    /// from the pair of their two ghosts in the parent's system across frames. Then the sensor moves at 0.5 m/s along -X,
    /// deeper into its own frame, for 1.5 s. The two part after 0.4 s, and each hears one CollisionEnded, as the two real
    /// bodies hear in one frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASensorAndABodyThatStickOutOfSiblingFramesAndOverlapOnlyOutsideBothBubblesHearOneCollisionAndOneEndWhenTheyPart(bool acrossFrames)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        PairScene scene = SensorAndBody(world, Space(world), acrossFrames, Ball());
        Tick(world, 10);
        if (acrossFrames)
            OnlyTheParentHoldsBoth(faults, scene);
        scene.SensorLog.NewsOf(scene.Body).Should().Equal(Started);
        scene.BodyLog.NewsOf(scene.Sensor).Should().Equal(Started);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();

        scene.SensorPilot.Once = body => body.SetVelocity(new Double3(-0.5, 0, 0));
        Tick(world, 90);

        world.GetFrame(scene.Sensor).Should().Be(scene.First);
        scene.SensorLog.NewsOf(scene.Body).Should().Equal(Started, Ended);
        scene.BodyLog.NewsOf(scene.Sensor).Should().Equal(Started, Ended);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Two dynamic sensors in the scene of <see cref="SensorAndBody"/>, which overlap only through their ghosts across
    /// frames, hear what two real dynamic sensors at the same places hear in one frame, also after the first one moves away.
    /// </summary>
    [Fact]
    public void TwoSensorsThatOverlapOnlyThroughTheirGhostsReportWhatTwoRealSensorsReportInOneFrame()
    {
        (List<ContactNews> First, List<ContactNews> Second) Run(bool acrossFrames)
        {
            using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
            PairScene scene = SensorAndBody(world, Space(world), acrossFrames, SensorBall);
            Tick(world, 10);
            if (acrossFrames)
                OnlyTheParentHoldsBoth(faults, scene);
            scene.SensorPilot.Once = body => body.SetVelocity(new Double3(-0.5, 0, 0));
            Tick(world, 90);
            faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
            world.Errors.Should().BeEmpty();
            return (scene.SensorLog.NewsOf(scene.Body), scene.BodyLog.NewsOf(scene.Sensor));
        }

        var inOneFrame = Run(acrossFrames: false);
        var acrossFrames = Run(acrossFrames: true);

        acrossFrames.First.Should().Equal(inOneFrame.First);
        acrossFrames.Second.Should().Equal(inOneFrame.Second);
    }

    // ---- A sensor in a ship that passes a static body ----

    /// <summary>Where <see cref="BallPassingAStaticBox"/> puts the ball.</summary>
    public enum SensorPlace
    {
        /// <summary>In space, which also holds the box: the run in one frame.</summary>
        Space,
        /// <summary>In the ship's frame.</summary>
        Ship,
        /// <summary>In a room inside the ship, out of whose bubble it sticks into the ship's frame.</summary>
        Room,
    }

    /// <summary>
    /// Space holds a static box of 1 m at (0, 11.3, 0), a sensor when <paramref name="boxIsSensor"/> is true. A ship flies
    /// past it along +X at 4 m/s from x = -8 m: a dynamic hull of 0.5 m with a bubble of 20 m. A ball of radius 0.5 m of
    /// <paramref name="ballBody"/> rests at (0, 11.3, 0) in the ship, at the box's height: in the ship's frame, or in a room,
    /// a frame without a body at (0, 8, 0) in the ship's frame with a bubble of 3 m, at (0, 3.3, 0) there, so that it sticks
    /// 0.8 m out of the room into the ship's frame. The box passes through the ship's bubble, so the ship's frame holds a
    /// ghost of the box, which moves there at -4 m/s, so it is kinematic in Jolt. In the room, the ball has a stick-out
    /// ghost in the ship's frame. When the ball is not dynamic, Jolt makes a contact of the box's ghost and the ball, or its
    /// ghost, only when one of the two kinematic bodies is a sensor, and the filter judges it by the motions of the real
    /// bodies. The two overlap while the ship is less than 1 m from the box along X. In space, the ball flies the same way
    /// past the box. Returns what the ball and the box heard of each other.
    /// </summary>
    private static (List<ContactNews> BallHeard, List<ContactNews> BoxHeard) BallPassingAStaticBox(SensorPlace place, BodyDesc ballBody,
        bool boxIsSensor = false)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        var boxLog = new CollisionLog();
        Entity box = world.Spawn(new SpawnDesc
        {
            Frame = space, Pose = At(0, 11.3), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) { IsSensor = boxIsSensor },
            Components = [boxLog],
        });
        FrameHandle ship = FrameHandle.Null, room = FrameHandle.Null;
        var ballLog = new CollisionLog();
        Entity ball;
        if (place == SensorPlace.Space)
        {
            ball = world.Spawn(new SpawnDesc
            {
                Frame = space, Pose = At(-8, 11.3), LinearVelocity = new Vector3(4, 0, 0), Body = ballBody, Components = [ballLog],
            });
        }
        else
        {
            ship = world.GetOwnedFrame(world.Spawn(new SpawnDesc
            {
                Frame = space,
                Pose = At(-8),
                LinearVelocity = new Vector3(4, 0, 0),
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.25f)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
                AsFrame = new FrameSpec(FrameBounds.Sphere(20)),
            }));
            if (place == SensorPlace.Room)
                room = Bubble(world, ship, At(0, 8), 3);
            ball = world.Spawn(place == SensorPlace.Room
                ? new SpawnDesc { Frame = room, Pose = At(0, 3.3), Body = ballBody, Components = [ballLog] }
                : new SpawnDesc { Frame = ship, Pose = At(0, 11.3), Body = ballBody, Components = [ballLog] });
        }

        bool ghostOfTheBox = false, ghostOfTheBall = false;
        for (int tick = 0; tick < 180; tick++)
        {
            world.Tick();
            if (place == SensorPlace.Space)
                continue;
            ghostOfTheBox |= faults.Jolt.HasGhostIn(box, ship);
            ghostOfTheBall |= faults.Jolt.HasGhostIn(ball, ship);
            if (place == SensorPlace.Room)
                faults.Jolt.HasGhostIn(box, room).Should().BeFalse("the room is no child of the box's frame");
        }

        if (place != SensorPlace.Space)
        {
            ghostOfTheBox.Should().BeTrue();
            ghostOfTheBall.Should().Be(place == SensorPlace.Room);
            world.GetFrame(ball).Should().Be(place == SensorPlace.Room ? room : ship);
        }
        world.PoseIn(ball, space).Position.X.Should().BeInRange(3.5, 4.5, "the ball flew 12 m in 3 s");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
        return (ballLog.NewsOf(box), boxLog.NewsOf(ball));
    }

    /// <summary>
    /// A dynamic sensor in the room of <see cref="BallPassingAStaticBox"/> detects the moving ghost of the static box
    /// through its own stick-out ghost, as a dynamic sensor that flies past the box in space detects the box: a dynamic
    /// sensor meets a static body.
    /// </summary>
    [Theory]
    [InlineData(SensorPlace.Room)]
    [InlineData(SensorPlace.Space)]
    public void ADynamicSensorThatSticksOutOfARoomInsideAShipDetectsTheMovingGhostOfAStaticBodyOfTheShipsParent(SensorPlace place)
    {
        var heard = BallPassingAStaticBox(place, SensorBall);

        heard.BallHeard.Should().Equal(Started, Ended);
        heard.BoxHeard.Should().Equal(Started, Ended);
    }

    /// <summary>
    /// A kinematic sensor in the ship of <see cref="BallPassingAStaticBox"/>, in the ship's frame or in the room, does not
    /// detect the moving ghost of the static box, as a kinematic sensor that flies past the box in space does not detect the
    /// box: Jolt makes no contact of a kinematic body and a static one, sensor or not. The box's ghost is kinematic in Jolt,
    /// which meets a kinematic sensor, so the filter refuses the pair by the box's static motion.
    /// </summary>
    [Theory]
    [InlineData(SensorPlace.Ship)]
    [InlineData(SensorPlace.Room)]
    [InlineData(SensorPlace.Space)]
    public void AKinematicSensorInAShipDoesNotDetectTheMovingGhostOfAStaticBodyOfTheShipsParent(SensorPlace place)
    {
        var heard = BallPassingAStaticBox(place, SensorBall with { Motion = BodyMotion.Kinematic });

        heard.BallHeard.Should().BeEmpty();
        heard.BoxHeard.Should().BeEmpty();
    }

    /// <summary>
    /// A static sensor box of space in <see cref="BallPassingAStaticBox"/> detects a kinematic ball in the ship, in the
    /// ship's frame or in the room, through the box's moving ghost, as it detects a kinematic ball that flies past it in
    /// space: Jolt makes a contact of a kinematic body and a sensor. Jolt sees two kinematic bodies, one a sensor, and the
    /// filter lets the pair through by the motions of the real bodies, a static sensor and a kinematic ball.
    /// </summary>
    [Theory]
    [InlineData(SensorPlace.Ship)]
    [InlineData(SensorPlace.Room)]
    [InlineData(SensorPlace.Space)]
    public void AStaticSensorDetectsAKinematicBodyInAPassingShipThroughItsMovingGhost(SensorPlace place)
    {
        var heard = BallPassingAStaticBox(place, Ball() with { Motion = BodyMotion.Kinematic }, boxIsSensor: true);

        heard.BallHeard.Should().Equal(Started, Ended);
        heard.BoxHeard.Should().Equal(Started, Ended);
    }

    // ---- The frame that holds the two ghosts goes ----

    /// <summary>
    /// The scene of <see cref="SensorAndBody"/> in a frame without a body of radius 50 m at the origin of space. Once the
    /// contact of the sensor and the ball has started, the game destroys that frame. Across frames, its system held the two
    /// ghosts, and the two bubbles move into space with the sensor and the ball. The disposal loosens the contact of the two
    /// ghosts, and the ghost pass of the next step makes their ghosts in space, which carry it, so the contact goes on, as
    /// the contact of the two real bodies goes on in one frame when they move into space. The ledger counts it right at every
    /// sync point.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AContactThroughTwoGhostsGoesOnWhenTheFrameThatHoldsTheGhostsIsDestroyed(bool acrossFrames)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        FrameHandle parent = Bubble(world, space, At(0), 50);
        PairScene scene = SensorAndBody(world, parent, acrossFrames, Ball());
        Tick(world, 10);
        if (acrossFrames)
            OnlyTheParentHoldsBoth(faults, scene);
        scene.SensorLog.NewsOf(scene.Body).Should().Equal(Started);

        world.Destroy(parent.Entity).Should().BeTrue();
        Tick(world, 10);

        if (acrossFrames)
        {
            world.Frames.Parent(scene.First).Should().Be(space);
            faults.Jolt.HasGhostIn(scene.Sensor, space).Should().BeTrue();
            faults.Jolt.HasGhostIn(scene.Body, space).Should().BeTrue();
        }
        world.GetFrame(scene.Sensor).Should().Be(acrossFrames ? scene.First : space);
        scene.SensorLog.NewsOf(scene.Body).Should().Equal(Started);
        scene.BodyLog.NewsOf(scene.Sensor).Should().Equal(Started);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }

    // ---- A numeric move replaces the ghosts ----

    /// <summary>
    /// The scene of <see cref="SensorAndBody"/>, but each bubble is the frame of a ship that flies along +Z at 60 m/s with
    /// a gentle thruster (see <see cref="Thruster"/>): a dynamic hull of 1 m and 1,000 kg at the bubble's center. The two
    /// hulls lie 7.4 m apart in space's system, within the merge test's reach of one system, which is their radii plus the
    /// split margin of 10 m. The sensor and the ball rest in their frames, and their contact starts through their ghosts in
    /// space. After 0.5 s of thrust, the hulls count as bodies that thrust, which get a numeric frame from 50 m/s, so the
    /// next numeric update moves both into one falling frame, and each takes along the ghost of the body that sticks out of
    /// its frame: the move replaces both ghosts. The carry of the next step finds the two new ghosts in the falling frame,
    /// so the contact goes on, as the contact of a real sensor and ball that fly the same way, with the same thrusters, goes
    /// on in one frame when they move into a falling frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AContactThroughTwoGhostsGoesOnWhenANumericMoveReplacesTheGhosts(bool acrossFrames)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        var velocity = new Vector3(0, 0, 60);
        Thruster Thrusting(float mass) => new(mass) { Direction = Vector3.UnitZ };
        FrameHandle first = space, second = space;
        if (acrossFrames)
        {
            var hull = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 1000, LinearDamping: 0, AngularDamping: 0);
            first = world.GetOwnedFrame(world.Spawn(new SpawnDesc
            {
                Frame = space, Pose = At(-3.7), LinearVelocity = velocity, Body = hull, AsFrame = new FrameSpec(FrameBounds.Sphere(3)),
                Components = [Thrusting(1000)],
            }));
            second = world.GetOwnedFrame(world.Spawn(new SpawnDesc
            {
                Frame = space, Pose = At(3.7), LinearVelocity = velocity, Body = hull, AsFrame = new FrameSpec(FrameBounds.Sphere(3)),
                Components = [Thrusting(1000)],
            }));
        }
        double firstX = acrossFrames ? -3.7 : 0, secondX = acrossFrames ? 3.7 : 0;
        Vector3 bodyVelocity = acrossFrames ? Vector3.Zero : velocity;
        var sensorLog = new CollisionLog();
        var ballLog = new CollisionLog();
        Entity sensor = world.Spawn(new SpawnDesc
        {
            Frame = first, Pose = At(-0.4 - firstX), LinearVelocity = bodyVelocity, Body = SensorBall,
            Components = acrossFrames ? [sensorLog] : [sensorLog, Thrusting(1)],
        });
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = second, Pose = At(0.4 - secondX), LinearVelocity = bodyVelocity, Body = Ball(),
            Components = acrossFrames ? [ballLog] : [ballLog, Thrusting(1)],
        });
        Entity firstMover = acrossFrames ? first.Entity : sensor, secondMover = acrossFrames ? second.Entity : ball;

        int ticks = TickUntil(world, () => world.Frames.Numeric(world.PhysicsHomeOf(firstMover)) == NumericMode.Falling, 60);

        ticks.Should().BeInRange(30, 40, "the bodies count as bodies that thrust after 0.5 s");
        sensorLog.NewsOf(ball).Should().Equal([Started], "the contact started before the move");
        FrameHandle falling = world.PhysicsHomeOf(firstMover);
        world.PhysicsHomeOf(secondMover).Should().Be(falling, "the two move in one falling frame");
        if (acrossFrames)
        {
            faults.Jolt.HasGhostIn(sensor, falling).Should().BeTrue("the move took the sensor's ghost along");
            faults.Jolt.HasGhostIn(ball, falling).Should().BeTrue("the move took the ball's ghost along");
            faults.Jolt.HasGhostIn(sensor, space).Should().BeFalse();
        }
        Tick(world, 10);

        if (acrossFrames)
        {
            world.GetFrame(sensor).Should().Be(first);
            world.GetFrame(ball).Should().Be(second);
        }
        sensorLog.NewsOf(ball).Should().Equal(Started);
        ballLog.NewsOf(sensor).Should().Equal(Started);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }
}
