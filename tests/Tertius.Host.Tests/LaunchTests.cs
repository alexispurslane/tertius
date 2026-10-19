using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// Launchable: a Launch sets the body's velocity relative to its frame. Here a crate floats at rest in a frame without
/// gravity that moves at 20 m/s along +X in its root frame.
/// </summary>
public sealed class LaunchTests : IDisposable
{
    private readonly World world = NewJoltWorld();
    private readonly FrameHandle frame;
    private readonly Entity crate;

    public LaunchTests()
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Entity ship = world.Spawn(new SpawnDesc
        {
            Frame = space,
            LinearVelocity = new Double3(20, 0, 0),
            Body = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(0.05f)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
        });
        frame = world.GetOwnedFrame(ship);
        crate = world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = new DPose(new Double3(0, 0, 10), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.2f)), Mass: 5, LinearDamping: 0, AngularDamping: 0),
            Components = [new Launchable()],
        });
        world.Tick();
    }

    public void Dispose() => world.Dispose();

    private void Launch(Double3 velocity) => world.Post(Event.Create(crate, Entity.Null, new Launch { Velocity = velocity }));

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            world.Tick();
    }

    [Fact]
    public void ALaunchSetsTheBodysVelocityRelativeToItsFrame()
    {
        Launch(new Double3(0, 0, -60));
        Ticks(3);

        Double3.Distance(world.VelocityIn(crate, frame), new Double3(0, 0, -60)).Should().BeLessThan(1e-4);
    }

    [Fact]
    public void TheLastLaunchOfATickCounts()
    {
        Launch(new Double3(0, 0, -60));
        Launch(new Double3(0, 5, 0));
        Ticks(3);

        Double3.Distance(world.VelocityIn(crate, frame), new Double3(0, 5, 0)).Should().BeLessThan(1e-4);
    }
}
