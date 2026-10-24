using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using static Tertius.Testing.WorldTesting;
using LaunchRamp = Tertius.Demo.DemoScenes.LaunchRamp;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The launch ramp of the demo scene demo-surface, on the World and the real Jolt backend, with no renderer: the parked
/// shuttle's ignition, and its slide from the bed under it onto the ramp's incline. The scene's person walks in the
/// terminal, far from the shuttle, through the whole test.
/// </summary>
public sealed class LaunchRampTests : IDisposable
{
    private readonly World world = NewJoltWorld();
    private readonly SceneInstance scene;
    private readonly Entity shuttle;
    private readonly Touches touches = new();

    public LaunchRampTests()
    {
        scene = DemoScenes.DemoSurface.Build(world, RenderResources.None);
        shuttle = scene["shuttle"];
        world.AddComponent(shuttle, touches);
        world.Tick();
    }

    public void Dispose() => world.Dispose();

    /// <summary>How far the hull's tail lies along -Z past the ramp's foot, in meters, in earth-surface.</summary>
    private double TailPastTheFoot()
    {
        DPose pose = world.PoseIn(shuttle, world.GetOwnedFrame(scene["earth-surface"]));
        return LaunchRamp.Foot.Z - pose.TransformPoint(new(0, -Shuttle.Hull.Y, Shuttle.Hull.Z)).Z;
    }

    /// <summary>
    /// The sled's rails are greased: from the ignition until its tail has passed the ramp's foot, the hull touches the ramp
    /// and nothing else, so no friction slows it. The slab round the ramp has friction.
    /// </summary>
    [Fact]
    public void TheHullTouchesOnlyTheGreasedRampOnItsWayOntoTheIncline()
    {
        world.Post(Event.Create(target: shuttle, sender: Entity.Null, payload: new Ignition()));

        TickUntil(world, () => TailPastTheFoot() > 1, 300);

        TailPastTheFoot().Should().BeGreaterThan(1, "the hull slid onto the incline");
        touches.Others.Should().Contain(scene["ramp"]);
        touches.Others.Should().NotContain(scene["floor"], "the slab has friction");
        touches.Others.Should().OnlyContain(other => other == scene["ramp"]);
    }

    /// <summary>
    /// The ignition starts the ramp in the same tick: the autopilot's state holds the guidance of the climb, made from
    /// Earth's G·M in the shuttle's frame, the parking orbit's radius and the engine's three gravities, and the distance
    /// from Earth's center at which the hull launched.
    /// </summary>
    [Fact]
    public void TheIgnitionStartsTheRampWithTheGuidanceOfTheClimbAndTheLaunchRadius()
    {
        AscentAutopilot autopilot = world.GetComponent<AscentAutopilot>(shuttle)!;
        autopilot.State.Value.Should().BeOfType<Parked>();

        world.Post(Event.Create(target: shuttle, sender: Entity.Null, payload: new Ignition()));
        TickUntil(world, () => autopilot.State.Value is not Parked, 3);

        autopilot.State.TryGetValue(out Ramp ramp).Should().BeTrue();
        ramp.Guidance.Mu.Should().Be(DemoScenes.EarthMu);
        ramp.Guidance.OrbitRadius.Should().Be(Shuttle.ParkingOrbitRadius);
        ramp.Guidance.MaxAcceleration.Should().BeApproximately(Shuttle.ThrustAcceleration, 1e-4);
        ramp.LaunchRadius.Should().BeApproximately(DemoScenes.DemoSurface.ShuttleParked.Position.Length, 1e-6);
    }

    /// <summary>A test component that records every entity whose touch of its entity starts.</summary>
    private sealed class Touches : IComponent
    {
        public HashSet<Entity> Others { get; } = [];

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Collision>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            foreach (ref Collision collision in events.Of<Collision>())
                Others.Add(collision.Other);
        }
    }
}
