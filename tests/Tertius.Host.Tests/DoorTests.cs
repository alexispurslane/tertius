using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// Door: a kinematic door slides between its closed pose, where it spawned, and its open pose. Here the door is a slab
/// 2 m tall that slides 2 m up, along its own +Y, in one second, in a room without gravity.
/// </summary>
public sealed class DoorTests : IDisposable
{
    // ---- A door that hears a sensor ----

    private const string Doorway = "door-tests/doorway";
    private static readonly Double3 Closed = new(1, 2, 3);

    private readonly World world = NewJoltWorld();
    private readonly FrameHandle room;
    private readonly Entity door;

    public DoorTests()
    {
        room = world.CreateRootFrame(new(FrameBounds.Sphere(1e4f)));
        door = SpawnDoor(DQuaternion.Identity);
        Ticks(5);
    }

    private double Height => Moved(door).Y;

    public void Dispose() => world.Dispose();

    private Entity SpawnDoor(DQuaternion rotation) => SpawnDoor(room, rotation);

    private Entity SpawnDoor(FrameHandle frame, DQuaternion rotation) =>
        world.Spawn(
            new()
            {
                Frame = frame,
                Pose = new(Closed, rotation),
                Body = new(Shape: new BoxShapeDesc(new(0.05f, 1, 0.5f)), Motion: BodyMotion.Kinematic),
                Components = [new Door(new(0, 2, 0), openSeconds: 1)],
            }
        );

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            world.Tick();
    }

    private void Order<T>(Entity to)
        where T : unmanaged, IEventPayload =>
        world.Post(Event.Create(target: to, sender: Entity.Null, payload: new T()));

    /// <summary>How far the door is from its closed pose, in meters, in the room's axes.</summary>
    private Double3 Moved(Entity of) => world.Snapshot.PoseInOwnFrame(of).Position - Closed;

    [Fact]
    public void ADoorStaysShutUntilAnOrderOpensIt()
    {
        Ticks(60);

        Moved(door).Length.Should().BeLessThan(1e-6);
    }

    [Fact]
    public void AnOpenOrderSlidesTheDoorAtASteadySpeedAndStopsItAtItsOpenPose()
    {
        Order<OpenDoor>(door);
        Ticks(20);
        double early = Height;
        Ticks(20);

        // 2 m in one second: a third of a meter in each 20 ticks.
        (Height - early)
            .Should()
            .BeApproximately(2.0 / 3, 1e-4);
        Ticks(40);
        Height.Should().BeApproximately(2, 1e-4);
        Ticks(30);
        Height.Should().BeApproximately(2, 1e-4, "an open door stays open");
        Moved(door).X.Should().BeApproximately(0, 1e-6);
        Moved(door).Z.Should().BeApproximately(0, 1e-6);
    }

    [Fact]
    public void ACloseOrderSlidesAnOpenDoorBackToItsClosedPose()
    {
        Order<OpenDoor>(door);
        Ticks(80);
        Order<CloseDoor>(door);
        Ticks(80);

        Moved(door).Length.Should().BeLessThan(1e-4);
    }

    [Fact]
    public void AnOrderMidwayTurnsTheDoorBackFromWhereItIs()
    {
        Order<OpenDoor>(door);
        Ticks(30);
        Order<CloseDoor>(door);
        double highest = Height;
        // The close order reaches the door within three ticks, while it still rises at 2 m/s.
        for (int i = 0; i < 10; i++)
        {
            Ticks(1);
            highest = Math.Max(highest, Height);
        }

        double turned = Height;

        (highest - turned).Should().BeGreaterThan(0.1, "the door turned back at once");
        highest.Should().BeLessThan(1.3, "the door rose for at most four ticks after the order, not to its open pose");
        // From under 1.3 m at 2 m/s, the door is shut well within a second.
        Ticks(60);
        Moved(door).Length.Should().BeLessThan(1e-4);
    }

    [Fact]
    public void ADoorSlidesAlongItsOwnAxes()
    {
        // A quarter turn about +Z takes the door's +Y to the room's -X.
        Entity turned = SpawnDoor(DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2));
        Ticks(5);
        Order<OpenDoor>(turned);
        Ticks(80);

        (Moved(turned) - new Double3(-2, 0, 0)).Length.Should().BeLessThan(1e-4);
    }

    [Fact]
    public void ADoorThatStartsOpenStaysWhereItSpawnedUntilACloseOrderSlidesItShutByItsSlide()
    {
        Entity open = world.Spawn(
            new()
            {
                Frame = room,
                Pose = new(new(5, 4, 3), DQuaternion.Identity),
                Body = new(Shape: new BoxShapeDesc(new(0.05f, 1, 0.5f)), Motion: BodyMotion.Kinematic),
                Components = [new Door(new(0, 2, 0), openSeconds: 1, openAtStart: true)],
            }
        );
        Ticks(30);
        Order<OpenDoor>(open);
        Ticks(30);
        (world.PoseIn(open, room).Position - new Double3(5, 4, 3))
            .Length.Should()
            .BeLessThan(1e-6, "an open door stays open, also when an open order comes");

        Order<CloseDoor>(open);
        Ticks(80);

        (world.PoseIn(open, room).Position - new Double3(5, 2, 3))
            .Length.Should()
            .BeLessThan(1e-4, "the door's closed pose lies its slide back from where it spawned");
    }

    // ---- A door that changes frames ----

    /// <summary>
    /// A static frame without a body in the room at (1, 5, 3), turned a quarter about +X, with a bubble of 0.5 m. The door's
    /// bounding sphere of 1.12 m touches the bubble when the door has slid 1.38 m up, after 0.69 s, and the door's open pose
    /// lies inside the bubble, so the door ends in the booth's frame.
    /// </summary>
    private Entity Booth() =>
        world.Spawn(
            new()
            {
                Frame = room,
                Pose = new(new(1, 5, 3), DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2)),
                AsFrame = new(FrameBounds.Sphere(0.5f)),
            }
        );

    /// <summary>
    /// Ticks <paramref name="count"/> times and returns how far <paramref name="of"/> strayed at the worst tick from its way
    /// in the room, from <paramref name="closed"/> to <paramref name="closed"/> plus <paramref name="slide"/>: from the line
    /// of the slide, or past its ends.
    /// </summary>
    private double WorstStray(Entity of, Double3 closed, Double3 slide, int count)
    {
        double worst = 0;
        Double3 along = slide / slide.Length;
        for (int i = 0; i < count; i++)
        {
            world.Tick();
            Double3 moved = world.PoseIn(of, room).Position - closed;
            double distance = Double3.Dot(moved, along);
            double stray = Math.Max((moved - along * distance).Length, Math.Max(-distance, distance - slide.Length));
            worst = Math.Max(worst, stray);
        }

        return worst;
    }

    [Fact]
    public void ADoorThatABubbleTakesInMidwaySlidesOnAlongItsWayInTheFrameThatItSpawnedIn()
    {
        FrameHandle booth = world.GetOwnedFrame(Booth());
        Order<OpenDoor>(door);

        double stray = WorstStray(of: door, closed: Closed, slide: new(0, 2, 0), count: 90);

        stray.Should().BeLessThan(1e-4, "the door slides on along its way in the room in the booth's frame");
        world.GetFrame(door).Should().Be(booth, "the door's bounding sphere touched the booth's bubble on its way up");
        (world.PoseIn(door, room).Position - Closed - new Double3(0, 2, 0))
            .Length.Should()
            .BeLessThan(1e-4, "the door reaches its open pose");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ADoorWhoseFrameDiesMidwayStopsOnItsWayInTheFrameThatTakesItIn()
    {
        // A cabin in the room, far from the other door, turned a quarter about +Z, so that the door's +Y is the room's -X there.
        var cabinPose = new DPose(new(100, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2));
        Entity cabin = world.Spawn(
            new()
            {
                Frame = room,
                Pose = cabinPose,
                AsFrame = new(FrameBounds.Sphere(20)),
            }
        );
        Entity inCabin = SpawnDoor(world.GetOwnedFrame(cabin), DQuaternion.Identity);
        Ticks(5);
        Order<OpenDoor>(inCabin);
        Ticks(30);
        world.Destroy(cabin);
        // The next step applies the last target, which the destroy converted into the room.
        world.Tick();
        Double3 stopped = world.PoseIn(inCabin, room).Position;

        Double3 closed = cabinPose.TransformPoint(Closed),
            slide = cabinPose.TransformDirection(new(0, 2, 0));
        double stray = WorstStray(of: inCabin, closed: closed, slide: slide, count: 60);

        (world.PoseIn(inCabin, room).Position - stopped)
            .Length.Should()
            .BeLessThan(1e-9, "the door's way died with the cabin, so it stays where it is");
        stray.Should().BeLessThan(1e-4, "the door stopped on its way");
        world.GetFrame(inCabin).Should().Be(room, "the room took the door in when the cabin died");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A static sensor 6 m from the door, at (-5, 2, 3), 1 m tall and deep and <paramref name="width"/> m wide along X,
    /// which relays to the topic <see cref="Doorway"/>, and the door subscribes to that topic.
    /// </summary>
    private void SensorThatTheDoorHears(float width)
    {
        world.Subscribe(door, Topics.Get(Doorway));
        world.Spawn(
            new()
            {
                Frame = room,
                Pose = new(new(-5, 2, 3), DQuaternion.Identity),
                Body = new(Shape: new BoxShapeDesc(new(width / 2, 0.5f, 0.5f)), Motion: BodyMotion.Static)
                {
                    IsSensor = true,
                },
                Components = [new Sensor { Topic = Doorway }],
            }
        );
    }

    /// <summary>A ball with a radius of 0.25 m at (<paramref name="x"/>, 2, 2) that drifts along +Z at
    /// <paramref name="speed"/> m/s.</summary>
    private void DriftingBall(double x, float speed) =>
        world.Spawn(
            new()
            {
                Frame = room,
                Pose = new(new(x, 2, 2), DQuaternion.Identity),
                LinearVelocity = new Vector3(0, 0, speed),
                Body = new(Shape: new SphereShapeDesc(0.25f), Mass: 1, LinearDamping: 0, AngularDamping: 0),
            }
        );

    /// <summary>
    /// A ball drifts at 0.5 m/s through the sensor. It overlaps the sensor while its center is less than 0.75 m from the
    /// sensor's center along Z: from 0.5 s to 3.5 s. The door opens in one second on the relayed Entered and shuts in one
    /// second on the relayed Left.
    /// </summary>
    [Fact]
    public void ADoorOpensWhenASensorThatItHearsRelaysAnEnteredAndShutsWhenTheSensorRelaysTheLeft()
    {
        SensorThatTheDoorHears(width: 1);
        DriftingBall(-5, 0.5f);

        Ticks(150);
        Height.Should().BeApproximately(2, 1e-4, "the ball is in the sensor at 2.5 s, and the door opened");
        Ticks(210);
        Height.Should().BeApproximately(0, 1e-4, "the ball left the sensor at 3.5 s, and the door shut");
    }

    /// <summary>
    /// Two balls drift through a sensor 2 m wide, side by side: one at 1 m/s, inside from 0.25 s to 1.75 s, and one at
    /// 0.25 m/s, inside from 1 s to 7 s. The door stays open until the last of them left.
    /// </summary>
    [Fact]
    public void ADoorThatHearsTwoBodiesInASensorStaysOpenUntilBothLeft()
    {
        SensorThatTheDoorHears(width: 2);
        DriftingBall(-5.5, 1);
        DriftingBall(-4.5, 0.25f);

        Ticks(180);
        Height.Should().BeApproximately(2, 1e-4, "the slow ball is still in the sensor at 3 s");
        Ticks(360);
        Height.Should().BeApproximately(0, 1e-4, "both balls left the sensor by 7 s, and the door shut");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ADoorRefusesAnOpeningTimeThatIsNotPositiveAndFinite(float openSeconds)
    {
        Action create = () => _ = new Door(new(0, 2, 0), openSeconds);

        create
            .Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithParameterName("openSeconds")
            .WithMessage("*time to open*positive and finite*");
    }
}
