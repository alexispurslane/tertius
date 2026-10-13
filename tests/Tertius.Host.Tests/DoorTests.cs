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
    private static readonly Double3 Closed = new(1, 2, 3);

    private readonly World world = NewJoltWorld();
    private readonly FrameHandle room;
    private readonly Entity door;

    public DoorTests()
    {
        room = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f), MotionDriver.Static));
        door = SpawnDoor(DQuaternion.Identity);
        Ticks(5);
    }

    public void Dispose() => world.Dispose();

    private Entity SpawnDoor(DQuaternion rotation) => world.Spawn(new SpawnDesc
    {
        Frame = room,
        Pose = new DPose(Closed, rotation),
        Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.05f, 1, 0.5f)), BodyMotion.Kinematic),
        Components = [new Door(new Vector3(0, 2, 0), openSeconds: 1)],
    });

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            world.Tick();
    }

    private void Order<T>(Entity to) where T : unmanaged, IEventPayload => world.Post(Event.Create(to, Entity.Null, new T()));

    /// <summary>How far the door is from its closed pose, in meters, in the room's axes.</summary>
    private Double3 Moved(Entity of) => world.Snapshot.PoseInOwnFrame(of).Position - Closed;

    private double Height => Moved(door).Y;

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
        (Height - early).Should().BeApproximately(2.0 / 3, 1e-4);
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

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ADoorRefusesAnOpeningTimeThatIsNotPositiveAndFinite(float openSeconds)
    {
        Action create = () => _ = new Door(new Vector3(0, 2, 0), openSeconds);

        create.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("openSeconds").WithMessage("*time to open*positive and finite*");
    }
}
