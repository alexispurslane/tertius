using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo shuttle's translation thrusters, on the World and the real Jolt backend (docs/plans/blue-danube-demo.md, "The
/// translation thrusters"): in each tick that brings a <see cref="Translation"/>, they push the hull with its force, limited
/// on each of the hull's own axes to their maximum, and keep nothing from an earlier tick. The shuttle starts parked in a
/// root frame without gravity, and unparks on its first tick.
/// </summary>
public sealed class TranslationThrusterTests : IDisposable
{
    private readonly World world = HostTesting.NewJoltWorld();
    private readonly FrameHandle space;
    private Entity shuttle;

    public TranslationThrusterTests() => space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));

    public void Dispose() => world.Dispose();

    private void Launch(DQuaternion turn, params IComponent[] components) =>
        shuttle = Shuttle.Park(world, space, new DPose(Double3.Zero, turn), components: [new Unpark(), .. components]);

    private static TranslationThrusters Thrusters() => new(Shuttle.TranslationThrust);

    /// <summary>The shuttle's acceleration over <paramref name="ticks"/> ticks, in m/s², in the axes of its frame.</summary>
    private Double3 AccelerationOver(int ticks)
    {
        Double3 before = world.VelocityIn(shuttle, space);
        Tick(world, ticks);
        return (world.VelocityIn(shuttle, space) - before) / (ticks * world.DeltaTime);
    }

    private static void ShouldBeNear(Double3 actual, Double3 expected, double tolerance)
    {
        actual.X.Should().BeApproximately(expected.X, tolerance, $"the acceleration is {actual}, not {expected}");
        actual.Y.Should().BeApproximately(expected.Y, tolerance, $"the acceleration is {actual}, not {expected}");
        actual.Z.Should().BeApproximately(expected.Z, tolerance, $"the acceleration is {actual}, not {expected}");
    }

    /// <summary>
    /// The hull is turned a quarter turn about its roof, and the force lies within the limit on every axis of the hull, so
    /// the hull moves with the force, in the axes of its frame, divided by its mass.
    /// </summary>
    [Fact]
    public void TheThrustersPushTheHullWithTheForceOfTheTick()
    {
        var force = new Double3(1000, -2000, 3000);
        Launch(DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2), new TranslationFor(int.MaxValue, force), Thrusters());
        Tick(world, 3);

        ShouldBeNear(AccelerationOver(30), force / Shuttle.Mass, 1e-4);
    }

    /// <summary>
    /// The hull is turned 45 degrees about its roof, and the force of 10,000 N points along the frame's +X. In the hull's
    /// axes it is 7,071 N along both +X and +Z, and each is limited to the 4,000 N of <see cref="Shuttle.TranslationThrust"/>.
    /// So the hull gets 5,657 N along the frame's +X, not the 4,000 N of a limit in the frame's axes.
    /// </summary>
    [Fact]
    public void EachAxisOfTheHullIsLimitedToTheThrustersMaximum()
    {
        Launch(DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 4), new TranslationFor(int.MaxValue, new Double3(10000, 0, 0)), Thrusters());
        Tick(world, 3);

        double limited = Shuttle.TranslationThrust * Math.Sqrt(2);
        ShouldBeNear(AccelerationOver(30), new Double3(limited / Shuttle.Mass, 0, 0), 1e-4);
    }

    /// <summary>The translation stops after 30 ticks, so the thrusters stop: they keep no force from an earlier tick.</summary>
    [Fact]
    public void ATickWithoutATranslationGivesNoForce()
    {
        Launch(DQuaternion.Identity, new TranslationFor(30, new Double3(0, 0, 3000)), Thrusters());
        Tick(world, 40);

        world.VelocityIn(shuttle, space).Length.Should().BeGreaterThan(0.05, "the thrusters pushed while the translation came");
        AccelerationOver(30).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>When one tick brings two translations, the last one counts, so a component that runs later overrides an earlier one.</summary>
    [Fact]
    public void TheLastTranslationOfATickCounts()
    {
        Launch(DQuaternion.Identity, new TranslationFor(int.MaxValue, new Double3(3000, 0, 0)), new TranslationFor(int.MaxValue, new Double3(0, 0, -2000)), Thrusters());
        Tick(world, 3);

        ShouldBeNear(AccelerationOver(30), new Double3(0, 0, -2000 / Shuttle.Mass), 1e-4);
    }

    [Fact]
    public void ANonFiniteTranslationIsAnErrorAndGivesNoForce()
    {
        Launch(DQuaternion.Identity, new TranslationFor(int.MaxValue, new Double3(double.NaN, 0, 1000)), Thrusters());

        Tick(world, 30);

        world.VelocityIn(shuttle, space).Length.Should().BeLessThan(1e-6);
        world.Errors.Should().Contain(error => error.Contains(nameof(TranslationThrusters)) && error.Contains("A translation is finite"));
    }

    /// <summary>
    /// A layer of the flight program at priority 2, as the docking autopilot of step 5 will be, adds the translation, and the
    /// thrusters, an actuator, run after every layer, so they see it.
    /// </summary>
    [Fact]
    public void TheThrustersRunAfterTheLayersOfTheFlightProgram()
    {
        Launch(DQuaternion.Identity, new TranslationFor(int.MaxValue, new Double3(0, 2000, 0), priority: 2), Thrusters());
        Tick(world, 3);

        ShouldBeNear(AccelerationOver(30), new Double3(0, 2000 / Shuttle.Mass, 0), 1e-4);
    }

    /// <summary>
    /// The shuttle's flight controls carry translation thrusters of 4,000 N per axis: a layer asks for 8,000 N along the
    /// nose and gets 4,000 N, 0.2 m/s² for the shuttle's 20 t.
    /// </summary>
    [Fact]
    public void TheShuttlesFlightControlsPushWith4000NPerAxis()
    {
        shuttle = Shuttle.Park(world, space, DPose.Identity,
            components: [new Unpark(), new TranslationFor(int.MaxValue, new Double3(0, 0, -8000), priority: 1), .. Shuttle.FlightControls(climbHeight: 30, planeNormal: -Double3.UnitX, station: Entity.Null)]);
        Tick(world, 3);

        ShouldBeNear(AccelerationOver(30), new Double3(0, 0, -0.2), 1e-4);
    }

    /// <summary>A test component that adds a <see cref="Translation"/> of <paramref name="force"/> to its entity's events on each of its first <paramref name="ticks"/> ticks.</summary>
    private sealed class TranslationFor(int ticks, Double3 force, int priority = 0) : IComponent
    {
        private int handled;

        public int Priority => priority;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (handled++ < ticks)
                events.Add(new Translation { Force = force });
        }
    }
}
