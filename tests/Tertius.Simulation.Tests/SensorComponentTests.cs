using System.Numerics;
using AwesomeAssertions;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The <see cref="Sensor"/> component relays the touches of its entity. When its entity hears a <see cref="Collision"/>
/// with an entity that its filter matches, it sends an <see cref="Entered"/>, and when that touch ends with a
/// <see cref="CollisionEnded"/>, a <see cref="Left"/>. Both name the sensor's entity and the other entity. They go to the
/// subscribers of the sensor's topic, or to every entity when it names none. A touch in the step of tick N reaches the
/// sensor's entity in tick N + 1, and the relayed message reaches its recipients in tick N + 2. The tests with physics run
/// the world on real Jolt. The tests of the relay alone post the contact events to a world without physics.
/// </summary>
public class SensorComponentTests
{
    /// <summary>What a recipient heard from a sensor: the tick, whether the other entity entered or left, the sensor and the other entity.</summary>
    private readonly record struct Relayed(long Tick, bool Entered, Entity Sensor, Entity Other);

    private static Recorder Listener() => new(EventMask.Of<Entered>().And<Left>());

    private static List<Relayed> RelayedTo(Recorder listener) => listener.Seen.Select(seen => seen.Event.Is<Entered>()
        ? new Relayed(seen.Tick, true, seen.Event.As<Entered>().Sensor, seen.Event.As<Entered>().Other)
        : new Relayed(seen.Tick, false, seen.Event.As<Left>().Sensor, seen.Event.As<Left>().Other)).ToList();

    /// <summary>A static sensor box with the half extents <paramref name="half"/>.</summary>
    private static BodyDesc SensorBox(Vector3 half) => new(new BoxShapeDesc(half), BodyMotion.Static) { IsSensor = true };

    /// <summary>
    /// A ball with a radius of 0.25 m that flies at 3 m/s along +X from x = -1.02 m and <paramref name="z"/>, in a frame with no
    /// gravity. It passes through a sensor cube 1 m on a side at the origin in half a second.
    /// </summary>
    private static Entity FlyingBall(World world, FrameHandle space, double z = 0, params IComponent[] components) => world.Spawn(new SpawnDesc
    {
        Frame = space,
        Pose = At(-1.02, 0, z),
        LinearVelocity = new Vector3(3, 0, 0),
        Body = new BodyDesc(new SphereShapeDesc(0.25f), Mass: 1, LinearDamping: 0, AngularDamping: 0),
        Components = components,
    });

    /// <summary>An entity without a body, 50 m away from everything, with <paramref name="listener"/>.</summary>
    private static Entity Bystander(World world, FrameHandle space, Recorder listener, params Topic[] subscriptions) =>
        world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, 50), Components = [listener], Subscriptions = subscriptions });

    // ---- With physics ----

    [Fact]
    public void ASensorRelaysAnEnteredAndALeftToTheSubscribersOfItsTopicOneTickAfterItsEntityHearsTheTouch()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        var touches = new CollisionLog();
        Entity sensor = world.Spawn(new SpawnDesc
        {
            Frame = space, Body = SensorBox(new Vector3(0.5f)), Components = [new Sensor { Topic = "sensor-tests/plates" }, touches],
        });
        Recorder subscriber = Listener(), bystander = Listener();
        Bystander(world, space, subscriber, Topics.Get("sensor-tests/plates"));
        Bystander(world, space, bystander);
        Entity ball = FlyingBall(world, space);

        Tick(world, 60);

        long started = touches.Seen.Single().Tick, ended = touches.Ended.Single().Tick;
        RelayedTo(subscriber).Should().Equal(new Relayed(started + 1, true, sensor, ball), new Relayed(ended + 1, false, sensor, ball));
        bystander.Seen.Should().BeEmpty("an entity that does not subscribe to the topic hears nothing");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ASensorWithoutATopicRelaysItsEnteredAndLeftToEveryEntity()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        Entity sensor = world.Spawn(new SpawnDesc { Frame = space, Body = SensorBox(new Vector3(0.5f)), Components = [new Sensor()] });
        Recorder first = Listener(), second = Listener(), ballListener = Listener();
        Bystander(world, space, first);
        Bystander(world, space, second);
        Entity ball = FlyingBall(world, space, components: [ballListener]);

        Tick(world, 60);

        foreach (Recorder listener in new[] { first, second, ballListener })
            RelayedTo(listener).Select(r => (r.Entered, r.Sensor, r.Other)).Should().Equal((true, sensor, ball), (false, sensor, ball));
        world.Errors.Should().BeEmpty();
    }

    /// <summary>Two balls fly side by side through a sensor 4 m deep, one of them crew. The sensor's filter matches crew only.</summary>
    [Fact]
    public void ASensorsFilterDropsTheTouchesOfAnEntityThatFailsItsQuery()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        var touches = new CollisionLog();
        world.Spawn(new SpawnDesc
        {
            Frame = space, Body = SensorBox(new Vector3(0.5f, 0.5f, 2)), Components = [new Sensor { Filter = EntityQuery.With<Crew>() }, touches],
        });
        Recorder listener = Listener();
        Bystander(world, space, listener);
        Entity crew = FlyingBall(world, space, z: -1, [new Crew()]);
        Entity cargo = FlyingBall(world, space, z: 1);

        Tick(world, 60);

        touches.NewsOf(cargo).Should().HaveCount(2, "the sensor's entity heard the cargo touch it and leave");
        RelayedTo(listener).Select(r => (r.Entered, r.Other)).Should().Equal((true, crew), (false, crew));
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The Sensor component works on a solid body too. With no gravity, a ball that bounces back fully flies into a
    /// static wall at 3 m/s and back out.
    /// </summary>
    [Fact]
    public void ASensorOnASolidBodyRelaysItsTouches()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        Entity wall = world.Spawn(new SpawnDesc
        {
            Frame = space, Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static), Components = [new Sensor()],
        });
        Recorder listener = Listener();
        Bystander(world, space, listener);
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = space, Pose = At(-1.02), LinearVelocity = new Vector3(3, 0, 0),
            Body = new BodyDesc(new SphereShapeDesc(0.25f), Mass: 1, Restitution: 1, LinearDamping: 0, AngularDamping: 0),
        });

        Tick(world, 60);

        world.PoseInOwnFrame(ball).Position.X.Should().BeLessThan(-1, "the wall sent the ball back");
        RelayedTo(listener).Select(r => (r.Entered, r.Sensor, r.Other)).Should().Equal((true, wall, ball), (false, wall, ball));
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A crew ball flies into a sensor that relays crew only and is destroyed there. Destroying a body ends its contacts,
    /// so the sensor relays a Left that names the destroyed entity, which no filter matches any more.
    /// </summary>
    [Fact]
    public void ASensorRelaysALeftForAnEntityThatIsDestroyedWhileItTouchesTheSensor()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        Entity sensor = world.Spawn(new SpawnDesc
        {
            Frame = space, Body = SensorBox(new Vector3(0.5f)), Components = [new Sensor { Filter = EntityQuery.With<Crew>() }],
        });
        Recorder listener = Listener();
        Bystander(world, space, listener);
        Entity ball = FlyingBall(world, space, components: [new Crew()]);
        Tick(world, 20);
        RelayedTo(listener).Select(r => r.Entered).Should().Equal([true], "the ball is inside the sensor");

        world.Destroy(ball);
        Tick(world, 5);

        world.IsAlive(ball).Should().BeFalse();
        RelayedTo(listener).Select(r => (r.Entered, r.Sensor, r.Other)).Should().Equal((true, sensor, ball), (false, sensor, ball));
        world.Errors.Should().BeEmpty();
    }

    // ---- The relay alone ----

    /// <summary>An entity without a body whose Sensor relays the touches of crew to every entity, in a world without physics.</summary>
    private static Entity CrewSensor(World world, FrameHandle root) =>
        world.Spawn(new SpawnDesc { Frame = root, Components = [new Sensor { Filter = EntityQuery.With<Crew>() }] });

    private static void Touch(World world, Entity sensor, Entity other) =>
        world.Post(Event.Create(sensor, Entity.Null, new Collision { Other = other }));

    private static void Part(World world, Entity sensor, Entity other) =>
        world.Post(Event.Create(sensor, Entity.Null, new CollisionEnded { Other = other }));

    /// <summary>
    /// The world routes the starts of a step before its ends. So when one contact with an entity ends and another one with
    /// it starts in the same step, the sensor's entity hears the new Collision first and then the old CollisionEnded. The
    /// entity never stopped touching the sensor, so the sensor relays nothing for that step, and a Left only when the last
    /// contact ends.
    /// </summary>
    [Fact]
    public void AContactThatEndsInTheStepInWhichAnotherWithTheSameEntityStartsRelaysNothing()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity sensor = CrewSensor(world, root);
        Recorder listener = Listener();
        world.Spawn(new SpawnDesc { Frame = root, Components = [listener] });
        Entity crew = world.Spawn(new SpawnDesc { Frame = root, Components = [new Crew()] });
        world.Tick();
        Touch(world, sensor, crew);
        Tick(world, 3);
        RelayedTo(listener).Select(r => r.Entered).Should().Equal([true]);

        Touch(world, sensor, crew);
        Part(world, sensor, crew);
        Tick(world, 3);
        RelayedTo(listener).Select(r => r.Entered).Should().Equal([true], "the entity still touches the sensor");

        Part(world, sensor, crew);
        Tick(world, 3);
        RelayedTo(listener).Select(r => (r.Entered, r.Other)).Should().Equal((true, crew), (false, crew));
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The filter decides at the start of a touch. An entity that becomes crew while it touches the sensor gets no Left,
    /// because the sensor relayed no Entered for it.
    /// </summary>
    [Fact]
    public void ASensorRelaysNoLeftForATouchWhoseStartItsFilterDropped()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity sensor = CrewSensor(world, root);
        Recorder listener = Listener();
        world.Spawn(new SpawnDesc { Frame = root, Components = [listener] });
        Entity cargo = world.Spawn(new SpawnDesc { Frame = root });
        world.Tick();
        Touch(world, sensor, cargo);
        Tick(world, 3);

        world.AddComponent(cargo, new Crew());
        Tick(world, 2);
        Part(world, sensor, cargo);
        Tick(world, 3);

        listener.Seen.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>Content names a sensor's topic and filter: a blueprint sets them, and the spawned sensor relays to that topic.</summary>
    [Fact]
    public void ASensorFromABlueprintRelaysToTheTopicThatTheBlueprintNames()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("plate", [new ComponentSpec(typeof(Sensor), new Dictionary<string, object?>
        {
            ["Topic"] = "sensor-tests/blueprint-plates",
            ["Filter"] = EntityQuery.With<Crew>(),
        })]));
        using World world = TestWorld.Create(out FrameHandle root);
        Entity plate = world.Spawn(blueprints.CreateSpawnDesc("plate", root, DPose.Identity));
        Recorder subscriber = Listener(), bystander = Listener();
        world.Spawn(new SpawnDesc { Frame = root, Components = [subscriber], Subscriptions = [Topics.Get("sensor-tests/blueprint-plates")] });
        world.Spawn(new SpawnDesc { Frame = root, Components = [bystander] });
        Entity crew = world.Spawn(new SpawnDesc { Frame = root, Components = [new Crew()] });
        Entity cargo = world.Spawn(new SpawnDesc { Frame = root });
        world.Tick();

        Touch(world, plate, crew);
        Touch(world, plate, cargo);
        Tick(world, 3);

        RelayedTo(subscriber).Select(r => (r.Entered, r.Sensor, r.Other)).Should().Equal((true, plate, crew));
        bystander.Seen.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>After an entity left, its next touch is a new one, which the sensor relays again.</summary>
    [Fact]
    public void AnEntityThatLeavesAndTouchesTheSensorAgainEntersAgain()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity sensor = CrewSensor(world, root);
        Recorder listener = Listener();
        world.Spawn(new SpawnDesc { Frame = root, Components = [listener] });
        Entity crew = world.Spawn(new SpawnDesc { Frame = root, Components = [new Crew()] });
        world.Tick();

        Touch(world, sensor, crew);
        Tick(world, 3);
        Part(world, sensor, crew);
        Tick(world, 3);
        Touch(world, sensor, crew);
        Tick(world, 3);

        RelayedTo(listener).Select(r => r.Entered).Should().Equal(true, false, true);
        world.Errors.Should().BeEmpty();
    }
}
