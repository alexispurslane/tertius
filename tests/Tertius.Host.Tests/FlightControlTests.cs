using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using static Tertius.Testing.WorldTesting;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo shuttle's main engine and attitude autopilot, on the World and the real Jolt backend: each reads what a
/// component of its own entity added to the entity's events in the same tick, a <see cref="Throttle"/> or an
/// <see cref="AttitudeTarget"/>, and keeps nothing from an earlier tick. The shuttle starts parked, unturned, in a root
/// frame without gravity, and unparks on its first tick.
/// </summary>
public sealed class FlightControlTests : IDisposable
{
    private readonly World world = NewJoltWorld();
    private readonly FrameHandle space;
    private Entity shuttle;

    public FlightControlTests() => space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));

    public void Dispose() => world.Dispose();

    private void Launch(params IComponent[] components) =>
        shuttle = Shuttle.Park(world, space, DPose.Identity, components: [new Unpark(), .. components]);

    private static MainEngine Engine() => new(Shuttle.Mass * Shuttle.ThrustAcceleration);

    /// <summary>The shuttle's acceleration over <paramref name="ticks"/> ticks, in m/s², in the axes of its frame.</summary>
    private Double3 AccelerationOver(int ticks)
    {
        Double3 before = world.VelocityIn(shuttle, space);
        Tick(world, ticks);
        return (world.VelocityIn(shuttle, space) - before) / (ticks * world.DeltaTime);
    }

    [Fact]
    public void TheEnginePushesTheHullAlongItsNoseWithTheThrottleOfTheTick()
    {
        Launch(new ThrottleFor(ticks: int.MaxValue, value: 0.5f), Engine());
        Tick(world, 3);

        Double3 acceleration = AccelerationOver(30);

        acceleration.Z.Should().BeApproximately(-0.5 * Shuttle.ThrustAcceleration, 0.01);
        Math.Abs(acceleration.X).Should().BeLessThan(1e-6);
        Math.Abs(acceleration.Y).Should().BeLessThan(1e-6);
    }

    /// <summary>The throttle stops after 30 ticks, so the engine stops: it keeps no throttle from an earlier tick.</summary>
    [Fact]
    public void ATickWithoutAThrottleGivesNoThrust()
    {
        Launch(new ThrottleFor(ticks: 30, value: 1), Engine());
        Tick(world, 40);

        world.VelocityIn(shuttle, space).Length.Should().BeGreaterThan(10, "the engine pushed while the throttle came");
        AccelerationOver(30).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>
    /// A throttle that a component adds after the engine has run comes too late for it, so the engine gives no thrust,
    /// rather than the throttle of the tick before.
    /// </summary>
    [Fact]
    public void AnEngineThatRunsBeforeTheComponentThatAddsItsThrottleGivesNoThrust()
    {
        Launch(new ThrottleFor(ticks: int.MaxValue, value: 1, priority: 2), Engine());

        Tick(world, 30);

        world.VelocityIn(shuttle, space).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>When one tick brings two throttles, the last one counts, so a component that runs later overrides an earlier one.</summary>
    [Fact]
    public void TheLastThrottleOfATickCounts()
    {
        Launch(new ThrottleFor(ticks: int.MaxValue, value: 1), new ThrottleFor(ticks: int.MaxValue, value: 0.25f), Engine());
        Tick(world, 3);

        AccelerationOver(30).Z.Should().BeApproximately(-0.25 * Shuttle.ThrustAcceleration, 0.01);
    }

    [Fact]
    public void AThrottleAboveFullIsAnErrorAndGivesNoThrust()
    {
        Launch(new ThrottleFor(ticks: int.MaxValue, value: 1.5f), Engine());

        Tick(world, 30);

        world.VelocityIn(shuttle, space).Length.Should().BeLessThan(1e-6);
        world.Errors.Should().Contain(error => error.Contains(nameof(MainEngine)) && error.Contains("A throttle lies from 0 to 1"));
    }

    /// <summary>
    /// A target a quarter turn about the roof's axis: the attitude autopilot turns the hull to it, at most
    /// <see cref="AttitudeAutopilot.MaxTurnRate"/>, about 10 s for the quarter turn, and holds it there.
    /// </summary>
    [Fact]
    public void TheAttitudeAutopilotTurnsTheHullToTheTargetOfEachTick()
    {
        DQuaternion target = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);
        Launch(new TargetFor(ticks: int.MaxValue, target), new AttitudeAutopilot(Shuttle.Inertia));

        Tick(world, 1200);

        double left = world.PoseIn(shuttle, space).Rotation.AngularVelocityTo(target, 1).Length;
        (left * 180 / Math.PI).Should().BeLessThan(1);
        world.AngularVelocityIn(shuttle, space).Length().Should().BeLessThan(0.01f);
    }

    /// <summary>The target stops after 60 ticks, while the hull turns, so the autopilot gives no more torque and the hull turns on at the rate it had.</summary>
    [Fact]
    public void ATickWithoutAnAttitudeTargetGivesNoTorque()
    {
        Launch(new TargetFor(ticks: 60, DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2)), new AttitudeAutopilot(Shuttle.Inertia));
        Tick(world, 70);
        System.Numerics.Vector3 turning = world.AngularVelocityIn(shuttle, space);

        Tick(world, 60);

        turning.Length().Should().BeGreaterThan(0.05f, "the autopilot turned the hull while the target came");
        (world.AngularVelocityIn(shuttle, space) - turning).Length().Should().BeLessThan(1e-4f);
    }

    /// <summary>
    /// The shuttle's frame has no point gravity to climb in, so the ascent autopilot refuses the ignition: it stays parked,
    /// and the hull stays static where it stood.
    /// </summary>
    [Fact]
    public void WithoutPointGravityTheAscentAutopilotStaysParked()
    {
        shuttle = Shuttle.Park(world, space, DPose.Identity, components: Shuttle.FlightControls(climbHeight: 30));
        world.Post(Event.Create(shuttle, Entity.Null, new Ignition()));

        Tick(world, 30);

        world.GetComponent<AscentAutopilot>(shuttle)!.State.Value.Should().BeOfType<Parked>();
        world.PoseIn(shuttle, space).Position.Length.Should().Be(0);
        world.Frames.HullMotion(world.GetOwnedFrame(shuttle)).Should().Be(BodyMotion.Static);
    }

    /// <summary>A test component that adds a <see cref="Throttle"/> of <paramref name="value"/> to its entity's events on each of its first <paramref name="ticks"/> ticks.</summary>
    private sealed class ThrottleFor(int ticks, float value, int priority = 0) : IComponent
    {
        private int handled;

        public int Priority => priority;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (handled++ < ticks)
                events.Add(new Throttle { Value = value });
        }
    }

    /// <summary>A test component that adds an <see cref="AttitudeTarget"/> of <paramref name="target"/> to its entity's events on each of its first <paramref name="ticks"/> ticks.</summary>
    private sealed class TargetFor(int ticks, DQuaternion target) : IComponent
    {
        private int handled;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (handled++ < ticks)
                events.Add(new AttitudeTarget { Rotation = target });
        }
    }
}
