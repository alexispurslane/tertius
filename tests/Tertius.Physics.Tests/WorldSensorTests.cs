using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Sensor bodies (<see cref="BodyDesc.IsSensor"/>) detect touches but do not push. A touch of a sensor starts in the first
/// step that begins with the two bodies overlapping, and it ends in the first step that begins without the overlap. Each
/// entity hears one Collision, with no impulse, and one CollisionEnded. A sensor detects dynamic and kinematic bodies and
/// characters. Sleep and wake send nothing. A ghost's touch is a touch of its entity, and an entity's body and its ghosts
/// make one touch.
/// </summary>
public class WorldSensorTests
{
    /// <summary>The length of a step, in seconds, at the world's 60 ticks a second.</summary>
    private const double StepSeconds = 1.0 / 60;

    private static readonly BodyDesc Person = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character,
        Mass: 70);

    /// <summary>A static sensor box with the half extents <paramref name="half"/>.</summary>
    private static BodyDesc SensorBox(Vector3 half) =>
        new(Shape: new BoxShapeDesc(half), Motion: BodyMotion.Static) { IsSensor = true };

    /// <summary>A ball with a radius of 0.25 m and a mass of 1 kg that nothing slows down.</summary>
    private static BodyDesc SmallBall(BodyMotion motion) => new(Shape: new SphereShapeDesc(0.25f), Motion: motion,
        Mass: 1,
        LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// With no gravity, a ball with a radius of 0.25 m flies at 3 m/s along +X, 0.05 m in each step, through a sensor cube
    /// 1 m on a side at the origin. Jolt finds touches by the positions at the start of a step, and step k starts with the
    /// ball's center at x0 + 0.05 m · k, from x0 = -1.02 m. The two overlap while the center is less than 0.75 m from the
    /// cube's center: from step 6, at -0.72 m, to step 35, at 0.73 m. A touch that a step finds reaches both entities in the
    /// next tick.
    /// </summary>
    [Theory]
    [InlineData(BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Kinematic)]
    public void ABodyThatFliesThroughASensorKeepsItsSpeedAndEachHearsOneStartAtTheOverlapAndOneEndAfterIt(
        BodyMotion motion)
    {
        const double x0 = -1.02, speed = 3, reach = 0.5 + 0.25;
        int firstOverlap = (int)Math.Ceiling((-reach - x0) / (speed * StepSeconds));
        int firstClear = (int)Math.Floor((reach - x0) / (speed * StepSeconds)) + 1;
        using World world = NewWorld(backend: out _);
        FrameHandle space = Ground(world, 0);
        var sensorLog = new CollisionLog();
        Entity sensor = world.Spawn(new() { Frame = space, Body = SensorBox(new(0.5f)), Components = [sensorLog] });
        var ballLog = new CollisionLog();
        var flyer = new SpawnDesc
        {
            Frame = space, Pose = At(x0), LinearVelocity = new Vector3((float)speed, 0, 0), Body = SmallBall(motion),
            Components = [ballLog]
        };
        // A kinematic body moves only by its motion intents, so it drifts by end poses.
        Entity ball = world.Spawn(motion == BodyMotion.Kinematic
            ? KinematicDrift.Drifting(flyer, world, flyer.LinearVelocity)
            : flyer);

        Tick(world, 60);

        sensorLog.NewsOf(ball).Should().Equal(Started, Ended);
        ballLog.NewsOf(sensor).Should().Equal(Started, Ended);
        sensorLog.Seen.Single().Tick.Should().Be(firstOverlap + 1,
            "the touch starts in the first step that begins with an overlap");
        sensorLog.Ended.Single().Tick.Should()
            .Be(firstClear + 1, "the touch ends in the first step that begins without one");
        ballLog.Seen.Single().Tick.Should().Be(firstOverlap + 1);
        ballLog.Ended.Single().Tick.Should().Be(firstClear + 1);
        world.VelocityInOwnFrame(ball).X.Should()
            .BeApproximately((float)speed, 1e-5f, "the sensor did not slow the ball");
        world.PoseInOwnFrame(ball).Position.X.Should().BeApproximately(x0 + 60 * speed * StepSeconds, 1e-4);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A sensor does not push, so its touch has no impulse. The ball of the test above, of 1 kg at 3 m/s, would hear
    /// 3 N·s from a static wall.
    /// </summary>
    [Fact]
    public void TheTouchOfASensorHasNoImpulse()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Ground(world, 0);
        var sensorLog = new CollisionLog();
        world.Spawn(new() { Frame = space, Body = SensorBox(new(0.5f)), Components = [sensorLog] });
        var ballLog = new CollisionLog();
        world.Spawn(new()
        {
            Frame = space, Pose = At(-1.02), LinearVelocity = new Vector3(3, 0, 0),
            Body = SmallBall(BodyMotion.Dynamic), Components = [ballLog]
        });

        Tick(world, 20);

        sensorLog.Seen.Should().ContainSingle().Which.Impulse.Should().Be(0);
        ballLog.Seen.Should().ContainSingle().Which.Impulse.Should().Be(0);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A character stands on a floor at x = -2 m, and then walks at 1.5 m/s along +X through a sensor 1 m wide and 2 m tall
    /// that stands on the floor at the origin. A walk that a tick orders moves the character in the next step, so 180
    /// ticks move it in 179 steps, 4.475 m. A solid box would stop the capsule, whose radius is 0.3 m, at x = -0.8 m.
    /// </summary>
    [Fact]
    public void ACharacterWalksThroughASensorAndEachHearsOneStartAndOneEnd()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var sensorLog = new CollisionLog();
        Entity sensor = world.Spawn(new()
            { Frame = ground, Pose = At(0, 1), Body = SensorBox(new(0.5f, 1, 0.5f)), Components = [sensorLog] });
        var pilot = new Pilot();
        var personLog = new CollisionLog();
        Entity person = world.Spawn(new()
            { Frame = ground, Pose = At(-2, 0.95), Body = Person, Components = [pilot, personLog] });
        Tick(world, 30);
        world.Snapshot.IsStanding(person).Should().BeTrue("the character stands on the floor");
        double start = world.PoseInOwnFrame(person).Position.X;

        pilot.EveryTick = body => body.MoveCharacter(new(1.5f, 0, 0));
        Tick(world, 180);

        world.PoseInOwnFrame(person).Position.X.Should().BeApproximately(start + 1.5 * 179 * StepSeconds, 0.01,
            "the sensor did not stop the character");
        sensorLog.NewsOf(person).Should().Equal(Started, Ended);
        personLog.NewsOf(sensor).Should().Equal(Started, Ended);
        personLog.Seen.Single(seen => seen.Other == sensor).Impulse.Should().Be(0, "a sensor pushes nothing");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A box rests on a floor inside a sensor that stands on the floor, falls asleep there, and wakes when a push moves it
    /// 5 cm/s. Jolt drops the pair of a sleeping body and a static sensor and finds it again on wake, which is no new
    /// touch. A body that moves far away keeps the system stepping, or nothing does.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABoxThatSleepsInASensorAndWakesThereHearsNoEndAndNoSecondStart(bool systemKeepsStepping)
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        if (systemKeepsStepping)
            world.Spawn(KinematicDrift.Drifting(
                new()
                {
                    Frame = ground, Pose = At(-40, 20),
                    Body = new(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Kinematic)
                },
                world, new(0, 0, 0.1)));
        var sensorLog = new CollisionLog();
        Entity sensor = world.Spawn(new()
            { Frame = ground, Pose = At(0, 1), Body = SensorBox(new(2, 1, 2)), Components = [sensorLog] });
        var boxLog = new CollisionLog();
        var pilot = new Pilot();
        Entity box = world.Spawn(new()
            { Frame = ground, Pose = At(0, 0.5), Body = Cube(), Components = [boxLog, pilot] });
        Tick(world, 1);
        TickUntil(world, () => !IsAwake(backend, ground, box), 600);
        IsAwake(backend, ground, box).Should().BeFalse("the box rests on the floor, so it falls asleep");
        Tick(world, 30);

        pilot.Once = body => body.AddImpulse(new(0.5f, 0, 0));
        Tick(world, 5);
        IsAwake(backend, ground, box).Should().BeTrue("the push woke the box");
        Tick(world, 30);

        sensorLog.NewsOf(box).Should().Equal([Started], "the box never left the sensor");
        boxLog.NewsOf(sensor).Should().Equal(Started);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// With no gravity, a kinematic block 2 m on a side sits in a bubble of radius 10 m at x = 9.5 m, so it sticks 0.5 m
    /// out of the bubble, and space holds a ghost of it. A static sensor in space, outside the bubble, spans x from 10.1 m
    /// to 14.5 m, so the ghost overlaps it. The block moves along +X at 2 m/s. It leaves the bubble when its bounding
    /// sphere, of radius √3 m, is outside the bubble by a margin of √3 m, at x = 13.46 m, inside the sensor. It leaves the
    /// sensor at x = 15.5 m. A ghost's touch is a touch of its entity: the sensor and the block hear one start while the
    /// block is still in its bubble. The block's own body takes the touch over at the hand-off, which starts nothing, and
    /// each hears one end when the block leaves the sensor.
    /// </summary>
    [Fact]
    public void ASensorHearsTheTouchOfABodysGhostOnceAndNoSecondTouchWhenTheBodyFollowsIt()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        FrameHandle bubble =
            world.GetOwnedFrame(world.Spawn(new() { Frame = space, AsFrame = new(FrameBounds.Sphere(10)) }));
        var sensorLog = new CollisionLog();
        Entity sensor = world.Spawn(new()
            { Frame = space, Pose = At(12.3), Body = SensorBox(new(2.2f, 1, 1)), Components = [sensorLog] });
        var blockLog = new CollisionLog();
        Entity block = world.Spawn(KinematicDrift.Drifting(new()
        {
            Frame = bubble, Pose = At(9.5),
            Body = new(Shape: new BoxShapeDesc(new(1)), Motion: BodyMotion.Kinematic), Components = [blockLog]
        }, world, new(2, 0, 0)));
        world.Tick();
        faults.Jolt.HasGhostIn(block, space).Should().BeTrue("the block sticks out of its bubble");

        TickUntil(world, () => world.GetFrame(block) == space, 300);
        world.GetFrame(block).Should().Be(space, "the block left its bubble");
        long handOff = world.TickNumber;
        sensorLog.NewsOf(block).Should()
            .Equal([Started], "the ghost touched the sensor while the block was in its bubble");
        blockLog.NewsOf(sensor).Should().Equal(Started);
        sensorLog.Seen.Single().Tick.Should().BeLessThan(handOff);
        Tick(world, 120);

        world.PoseInOwnFrame(block).Position.X.Should().BeGreaterThan(15.6, "the block passed the sensor");
        sensorLog.NewsOf(block).Should().Equal(Started, Ended);
        blockLog.NewsOf(sensor).Should().Equal(Started, Ended);
        sensorLog.Ended.Single().Tick.Should().BeGreaterThan(handOff);
        world.Errors.Should().BeEmpty();
    }
}
