using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// Airlock: a use cycles the airlock's two doors, so that one of them is always shut. Here each door is a slab that
/// slides 2 m up in half a second, and a cycle opens the other door one second after it closes the open one. The
/// room has no gravity.
/// </summary>
public sealed class AirlockTests : IDisposable
{
    private const int Second = 60;

    private readonly World world = NewJoltWorld();
    private readonly Entity inner, outer, airlock;

    public AirlockTests()
    {
        FrameHandle room = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f), MotionDriver.Static));
        inner = SpawnDoor(room, z: 0);
        outer = SpawnDoor(room, z: 3);
        airlock = world.Spawn(new SpawnDesc
        {
            Frame = room,
            Pose = new DPose(new Double3(0, 1, 1.5), DQuaternion.Identity),
            Components = [new Airlock(inner, outer, cycleSeconds: 1)],
        });
    }

    public void Dispose() => world.Dispose();

    private Entity SpawnDoor(FrameHandle room, double z) => world.Spawn(new SpawnDesc
    {
        Frame = room,
        Pose = new DPose(new Double3(0, 1, z), DQuaternion.Identity),
        Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.6f, 1, 0.05f)), BodyMotion.Kinematic),
        Components = [new Door(new Vector3(0, 2, 0), openSeconds: 0.5f)],
    });

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            world.Tick();
    }

    private void Use() => world.Post(Event.Create(airlock, Entity.Null, new Use { User = Entity.Null }));

    private double Height(Entity door) => world.Snapshot.PoseOf(door).Position.Y - 1;

    private void ExpectOpen(Entity door, string name) => Height(door).Should().BeApproximately(2, 1e-4, $"the {name} door is open");

    private void ExpectShut(Entity door, string name) => Height(door).Should().BeApproximately(0, 1e-4, $"the {name} door is shut");

    [Fact]
    public void AtItsStartTheAirlockOpensTheInnerDoorAndKeepsTheOuterDoorShut()
    {
        Ticks(Second);

        ExpectOpen(inner, "inner");
        ExpectShut(outer, "outer");
    }

    [Fact]
    public void AUseClosesTheOpenDoorAndOpensTheOtherOneACycleLater()
    {
        Ticks(Second);
        Use();
        // The inner door is shut after half a second. The outer door's open order comes one second after the inner
        // door's close order.
        Ticks(Second * 3 / 4);
        ExpectShut(inner, "inner");
        ExpectShut(outer, "outer");
        Ticks(Second);

        ExpectShut(inner, "inner");
        ExpectOpen(outer, "outer");
    }

    [Fact]
    public void AUseDuringACycleDoesNothing()
    {
        Ticks(Second);
        Use();
        Ticks(Second / 3);
        Use();
        Ticks(Second * 3);

        // A second cycle would have shut the outer door again and opened the inner one.
        ExpectShut(inner, "inner");
        ExpectOpen(outer, "outer");
    }

    [Fact]
    public void AUseAfterTheCycleCyclesTheAirlockBack()
    {
        Ticks(Second);
        Use();
        Ticks(Second * 2);
        Use();
        Ticks(Second * 3 / 4);
        ExpectShut(inner, "inner");
        ExpectShut(outer, "outer");
        Ticks(Second);

        ExpectOpen(inner, "inner");
        ExpectShut(outer, "outer");
    }
}
