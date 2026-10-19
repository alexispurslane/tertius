using Tertius.Demo;
using Tertius.Simulation;
using Tertius.Testing;
using static Tertius.Testing.WorldTesting;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The parked shuttle of the demo (<see cref="Shuttle"/>), on the World and the real Jolt backend. It stands at Earth's
/// north pole in a root frame with Earth's point gravity, unturned, so its +Y is up.
/// </summary>
public sealed class ShuttleTests : IDisposable
{
    private readonly World world = HostTesting.NewJoltWorld();

    public void Dispose() => world.Dispose();

    /// <summary>
    /// A ball is let go 1.5 m above the cabin floor. The shuttle's frame has no gravity field of its own, so the ball
    /// feels only the opposite of the frame's proper acceleration, which the engine derives from the parked hull's rest
    /// (ARCHITECTURE.md, "How a frame moves"). Over 15 ticks it gains the velocity of a fall with the gravity at the
    /// shuttle's place, G·M / r², about 9.82 m/s² down. A new frame reads no proper acceleration during its first step,
    /// so the measure starts after two ticks.
    /// </summary>
    [Fact]
    public void ABallInTheParkedCabinFallsWithThePlanetsGravity()
    {
        FrameHandle earth = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), GravityField.Point(DemoScenes.EarthMu)));
        double r = DemoScenes.EarthRadius + Shuttle.Hull.Y;
        FrameHandle cabin = world.GetOwnedFrame(Shuttle.Park(world, earth, new DPose(new Double3(0, r, 0), DQuaternion.Identity)));
        Entity ball = world.Spawn(new SpawnDesc { Frame = cabin, Pose = At(0, -Shuttle.Cabin.Y + 1.5), Body = Ball(0.1f) });
        Tick(world, 2);
        Double3 before = world.VelocityIn(ball, cabin);

        const int ticks = 15;
        Tick(world, ticks);
        Double3 acceleration = (world.VelocityIn(ball, cabin) - before) / (ticks * world.DeltaTime);

        double g = DemoScenes.EarthMu / (r * r);
        acceleration.Y.Should().BeApproximately(-g, 0.01, "the cabin's contents have the weight of the ground");
        Math.Abs(acceleration.X).Should().BeLessThan(0.01);
        Math.Abs(acceleration.Z).Should().BeLessThan(0.01);
    }
}
