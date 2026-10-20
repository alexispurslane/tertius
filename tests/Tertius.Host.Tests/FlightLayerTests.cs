using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using static Tertius.Testing.WorldTesting;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The shuttle's flight program as a stack of layers (docs/plans/blue-danube-demo.md, "Layers"), on the World and the real
/// Jolt backend. A layer is a component that adds a <see cref="Throttle"/> and an <see cref="AttitudeTarget"/> to its
/// entity's events in its tick. The layers run in the order of the flight, by their priorities: the ascent autopilot at 0,
/// the rendezvous autopilot at 1 and the docking autopilot at 2. The actuators, the <see cref="MainEngine"/> and the
/// <see cref="AttitudeAutopilot"/>, run after every layer and follow the last one that speaks. Each test lists the
/// actuators before the layers, so that only the priorities put them after the layers. The shuttle starts unturned in a
/// root frame without gravity, and unparks on its first tick.
/// </summary>
public sealed class FlightLayerTests : IDisposable
{
    /// <summary>A quarter turn about the shuttle's up, which turns the nose from -Z to -X.</summary>
    private static readonly DQuaternion QuarterTurn = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);

    private readonly World world = NewJoltWorld();
    private readonly FrameHandle space;
    private Entity shuttle;

    public FlightLayerTests() => space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));

    public void Dispose() => world.Dispose();

    private void Launch(params IComponent[] components) =>
        shuttle = Shuttle.Park(world, space, DPose.Identity, components: [new Unpark(), .. components]);

    /// <summary>The angle between the shuttle's nose and <paramref name="nose"/>, in degrees.</summary>
    private double DegreesFrom(Double3 nose)
    {
        Double3 actual = world.PoseIn(shuttle, space).Rotation.Rotate(-Double3.UnitZ);
        return Math.Acos(Math.Clamp(Double3.Dot(actual, nose.Normalized()), -1, 1)) * 180 / Math.PI;
    }

    /// <summary>
    /// The ascent layer, at 0, asks to hold the nose where it is, and the rendezvous layer, at 1, asks for a quarter turn.
    /// The attitude autopilot follows the later layer: in 20 s the nose turns onto -X.
    /// </summary>
    [Fact]
    public void ALaterLayersTargetOverridesAnEarlierLayersTarget()
    {
        Launch(new AttitudeAutopilot(Shuttle.Inertia), new Layer(priority: 0, target: DQuaternion.Identity),
            new Layer(priority: 1, target: QuarterTurn));

        Tick(world, 1200);

        DegreesFrom(-Double3.UnitX).Should().BeLessThan(1);
    }

    /// <summary>
    /// The docking layer, at 2, the last layer of the flight, asks for half throttle and a quarter turn. Both actuators
    /// read what it added: the engine pushes, and the nose turns onto -X.
    /// </summary>
    [Fact]
    public void TheActuatorsRunAfterEveryLayer()
    {
        Launch(new MainEngine(Shuttle.Mass * Shuttle.ThrustAcceleration), new AttitudeAutopilot(Shuttle.Inertia),
            new Layer(priority: 2, target: QuarterTurn, throttle: 0.5f));

        Tick(world, 1200);

        world.VelocityIn(shuttle, space).Length.Should().BeGreaterThan(100, "the engine pushed at half throttle");
        DegreesFrom(-Double3.UnitX).Should().BeLessThan(1);
    }

    /// <summary>
    /// A test layer of the flight program: in every tick it adds an <see cref="AttitudeTarget"/> of <paramref name="target"/>
    /// and, when <paramref name="throttle"/> is set, a <see cref="Throttle"/> of it, to its entity's events.
    /// </summary>
    private sealed class Layer(int priority, DQuaternion target, float? throttle = null) : IComponent
    {
        public int Priority => priority;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            events.Add(new AttitudeTarget { Rotation = target });
            if (throttle is float value)
                events.Add(new Throttle { Value = value });
        }
    }
}
