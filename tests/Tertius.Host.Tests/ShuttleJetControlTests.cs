using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo shuttle's jets as parts, on the World and the real Jolt backend (docs/plans/blue-danube-demo.md, "The jets as
/// parts"): fitting the jets spawns the main engine and the 24 jets as attached entities of the hull and adds their
/// <see cref="ShuttleJetControl"/>, which in each tick sends each part its throttle by message, from the <see cref="Throttle"/>, the
/// <see cref="Torque"/> and the <see cref="Translation"/> that the components before it added. Each part pushes the hull
/// from its place. The shuttle starts parked in a root frame without gravity, and unparks on its first tick.
/// </summary>
public sealed class ShuttleJetControlTests : IDisposable
{
    private readonly World world = HostTesting.NewJoltWorld();
    private readonly FrameHandle space;
    private Entity shuttle;

    public ShuttleJetControlTests() => space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));

    public void Dispose() => world.Dispose();

    /// <summary>Parks the shuttle turned by <paramref name="turn"/>, with <paramref name="components"/>, and fits its jets (see <see cref="Shuttle.FitJets"/>).</summary>
    private void Launch(DQuaternion turn, params IComponent[] components)
    {
        shuttle = Shuttle.Park(world, space, new DPose(Double3.Zero, turn), components: [new Unpark(), .. components]);
        Shuttle.FitJets(world, shuttle);
    }

    private ShuttleJetControl Control => world.GetComponent<ShuttleJetControl>(shuttle)!;

    /// <summary>The hull's center of mass, in meters, in its own axes, as the engine computes it.</summary>
    private Double3 CenterOfMass
    {
        get
        {
            world.Snapshot.TryGetBody(shuttle, out BodyFacts facts).Should().BeTrue();
            return (Double3)facts.CenterOfMass;
        }
    }

    /// <summary>Where the hull's center of mass is, in meters, in the frame's axes.</summary>
    private Double3 CenterOfMassInSpace => world.PoseIn(shuttle, space).TransformPoint(CenterOfMass);

    /// <summary>The shuttle's acceleration over <paramref name="ticks"/> ticks, in m/s², in the axes of its frame.</summary>
    private Double3 AccelerationOver(int ticks)
    {
        Double3 before = world.VelocityIn(shuttle, space);
        Tick(world, ticks);
        return (world.VelocityIn(shuttle, space) - before) / (ticks * world.DeltaTime);
    }

    private static void ShouldBeNear(Double3 actual, Double3 expected, double tolerance)
    {
        actual.X.Should().BeApproximately(expected.X, tolerance, $"the value is {actual}, not {expected}");
        actual.Y.Should().BeApproximately(expected.Y, tolerance, $"the value is {actual}, not {expected}");
        actual.Z.Should().BeApproximately(expected.Z, tolerance, $"the value is {actual}, not {expected}");
    }

    /// <summary>
    /// Fitting the jets places them for the engine's center of mass and spawns one part for the main engine and one for
    /// each of the 24 jets. Each part sits at its place on the turned hull, keeps the hull's rotation, and carries its own
    /// component: the <see cref="MainEngine"/>, or a <see cref="JetThruster"/> of its jet.
    /// </summary>
    [Fact]
    public void TheJetsArePartsOfTheHullAtTheirPlacesWithTheHullsRotation()
    {
        DQuaternion turn = DQuaternion.FromAxisAngle(new Double3(1, 2, -0.5).Normalized(), 0.7);
        Launch(turn);

        Tick(world, 2);

        ShuttleJets layout = Control.Layout;
        layout.CenterOfMass.Should().Be(CenterOfMass, "the jets are placed for the engine's center of mass");
        DPose hull = world.PoseIn(shuttle, space);
        void ShouldSitAt(Entity part, Double3 place)
        {
            world.IsAlive(part).Should().BeTrue();
            DPose pose = world.PoseIn(part, space);
            (pose.Position - hull.TransformPoint(place)).Length.Should().BeLessThan(1e-5, $"the part sits at {place} on the hull");
            hull.Rotation.AngularVelocityTo(pose.Rotation, 1).Length.Should().BeLessThan(1e-6, "the part keeps the hull's rotation");
        }
        ShouldSitAt(Control.MainEnginePart, layout.MainEnginePlace);
        world.GetComponent<MainEngine>(Control.MainEnginePart)!.MaxThrust.Should().Be(Shuttle.MainEngineThrust);
        Control.JetParts.Should().HaveCount(2 * ShuttleJets.PairCount);
        for (int pair = 0; pair < ShuttleJets.PairCount; pair++)
        {
            foreach ((Jet jet, bool second) in new[] { (layout.Pairs[pair].First, false), (layout.Pairs[pair].Second, true) })
            {
                Entity part = Control.JetParts[ShuttleJetControl.PartIndex(pair, second)];
                ShouldSitAt(part, jet.Place);
                world.GetComponent<JetThruster>(part)!.Jet.Should().Be(jet);
            }
        }
    }

    /// <summary>
    /// The hull is turned a quarter turn about its roof, and the force lies within the limit on every axis of the hull, so
    /// the hull moves with the force, in the axes of its frame, divided by its mass. The translation pairs push on lines
    /// through the center of mass, so the hull does not turn.
    /// </summary>
    [Fact]
    public void TheJetsPushTheHullWithTheTranslationOfTheTickAndDoNotTurnIt()
    {
        var force = new Double3(1000, -2000, 3000);
        Launch(DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2), new TranslationFor(int.MaxValue, force));
        Tick(world, 4);

        ShouldBeNear(AccelerationOver(30), force / Shuttle.Mass, 1e-4);
        world.AngularVelocityIn(shuttle, space).Length().Should().BeLessThan(1e-6f, "the translation pairs give no torque");
    }

    /// <summary>
    /// The hull is turned 45 degrees about its roof, and the force of 75,000 N points along the frame's +X. In the hull's
    /// axes it is 53,033 N along both +X and +Z, and each is limited to the 30,000 N of <see cref="Shuttle.TranslationThrust"/>.
    /// So the hull gets 42,426 N along the frame's +X, not the 30,000 N of a limit in the frame's axes.
    /// </summary>
    [Fact]
    public void EachAxisOfTheHullIsLimitedToTheThrustOfItsTranslationPair()
    {
        Launch(DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 4), new TranslationFor(int.MaxValue, new Double3(75000, 0, 0)));
        Tick(world, 4);

        double limited = Shuttle.TranslationThrust * Math.Sqrt(2);
        ShouldBeNear(AccelerationOver(30), new Double3(limited / Shuttle.Mass, 0, 0), 1e-4);
    }

    /// <summary>The translation stops after 30 ticks, so the jets stop: they keep no throttle from an earlier tick.</summary>
    [Fact]
    public void ATickWithoutATranslationFiresNoJet()
    {
        Launch(DQuaternion.Identity, new TranslationFor(30, new Double3(0, 0, 3000)));
        Tick(world, 40);

        world.VelocityIn(shuttle, space).Length.Should().BeGreaterThan(0.05, "the jets pushed while the translation came");
        AccelerationOver(30).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>When one tick brings two translations, the last one counts, so a component that runs later overrides an earlier one.</summary>
    [Fact]
    public void TheLastTranslationOfATickCounts()
    {
        Launch(DQuaternion.Identity, new TranslationFor(int.MaxValue, new Double3(3000, 0, 0)), new TranslationFor(int.MaxValue, new Double3(0, 0, -2000)));
        Tick(world, 4);

        ShouldBeNear(AccelerationOver(30), new Double3(0, 0, -2000 / Shuttle.Mass), 1e-4);
    }

    [Fact]
    public void ANonFiniteTranslationIsAnErrorAndFiresNoJet()
    {
        Launch(DQuaternion.Identity, new TranslationFor(int.MaxValue, new Double3(double.NaN, 0, 1000)));

        Tick(world, 30);

        world.VelocityIn(shuttle, space).Length.Should().BeLessThan(1e-6);
        world.Errors.Should().Contain(error => error.Contains(nameof(ShuttleJetControl)) && error.Contains("A force is finite"));
    }

    [Fact]
    public void ANonFiniteTorqueIsAnErrorAndFiresNoJet()
    {
        Launch(DQuaternion.Identity, new TorqueAt(0, int.MaxValue, new Double3(0, double.PositiveInfinity, 0), own: false));

        Tick(world, 30);

        world.AngularVelocityIn(shuttle, space).Length().Should().BeLessThan(1e-6f);
        world.Errors.Should().Contain(error => error.Contains(nameof(ShuttleJetControl)) && error.Contains("A torque is finite"));
    }

    /// <summary>
    /// A layer of the flight program at priority 2, as the docking autopilot is, adds the translation, and the control, which
    /// commands the actuators, runs after every layer, so it sees it.
    /// </summary>
    [Fact]
    public void TheControlRunsAfterTheLayersOfTheFlightProgram()
    {
        Launch(DQuaternion.Identity, new TranslationFor(int.MaxValue, new Double3(0, 2000, 0), priority: 2));
        Tick(world, 4);

        ShouldBeNear(AccelerationOver(30), new Double3(0, 2000 / Shuttle.Mass, 0), 1e-4);
    }

    /// <summary>
    /// The shuttle's flight controls carry translation pairs of 30,000 N per axis: a layer asks for 60,000 N along the
    /// nose and gets 30,000 N, 1.5 m/s² for the shuttle's 20 t.
    /// </summary>
    [Fact]
    public void TheShuttlesFlightControlsPushWith30000NPerAxis()
    {
        shuttle = Shuttle.Park(world, space, DPose.Identity,
            components: [new Unpark(), new TranslationFor(int.MaxValue, new Double3(0, 0, -60000), priority: 1), .. Shuttle.FlightControls(climbHeight: 30, plane: new LaunchPlane(space, -Double3.UnitX), station: Entity.Null)]);
        Shuttle.FitActuators(world, shuttle);
        Tick(world, 4);

        ShouldBeNear(AccelerationOver(30), new Double3(0, 0, -1.5), 1e-4);
    }

    /// <summary>
    /// A torque in the frame's axes, within the couples' most about every axis of the turned hull, 72,934 N·m about Z, for
    /// 20 ticks: the turning jets turn the hull as a torque on its own body does, one tick later, so after both pushes the
    /// two hulls turn at one angular velocity. Each couple gives no net force, so the center of mass of the hull with the
    /// jets moves less than a micrometer, the rounding of its pose. Measured with a hand mutation: when the second jet of
    /// every couple has 1 % more thrust than its partner, the center of mass moves 0.53 mm, and the spin is off by 0.7 %.
    /// </summary>
    [Fact]
    public void TheTurningJetsGiveTheTorqueOfTheTickAndMoveNotTheCenterOfMass()
    {
        var torque = new Double3(20000, -30000, 10000);
        DQuaternion turn = DQuaternion.FromAxisAngle(new Double3(0.3, 1, 0.2).Normalized(), 0.9);
        Entity own = Shuttle.Park(world, space, new DPose(new Double3(-1000, 0, 0), turn), components: [new Unpark(), new TorqueAt(3, 20, torque, own: true)]);
        Launch(turn, new TorqueAt(2, 20, torque, own: false));
        Tick(world, 1);
        Double3 start = CenterOfMassInSpace;

        Tick(world, 40);

        Double3 spin = (Double3)world.AngularVelocityIn(shuttle, space), ownSpin = (Double3)world.AngularVelocityIn(own, space);
        ownSpin.Length.Should().BeGreaterThan(0.01, "the torque turned the hull");
        (spin - ownSpin).Length.Should().BeLessThan(1e-3 * ownSpin.Length, $"the jets turn the hull at {spin}, as its own torque does at {ownSpin}");
        (CenterOfMassInSpace - start).Length.Should().BeLessThan(1e-6, "the couples give no net force");
    }

    /// <summary>
    /// The main engine's part sits on the line of thrust through the center of mass, so at half throttle it pushes the hull
    /// along its nose with half the thrust and turns it not at all.
    /// </summary>
    [Fact]
    public void TheMainEnginesPartPushesAlongTheNoseAndTurnsNothing()
    {
        Launch(DQuaternion.Identity, new ThrottleFor(0.5f));
        Tick(world, 4);

        Double3 acceleration = AccelerationOver(30);

        acceleration.Z.Should().BeApproximately(-0.5 * Shuttle.ThrustAcceleration, 0.01);
        world.AngularVelocityIn(shuttle, space).Length().Should().BeLessThan(1e-7f, "the main engine pushes through the center of mass");
    }

    /// <summary>
    /// One hull pushes its own body in tick 5, and another asks its jets for the same force in tick 5. The jets get their
    /// throttles by message, so the second hull starts to move one tick after the first.
    /// </summary>
    [Fact]
    public void AJetPushesOneTickAfterTheTickThatCommandsIt()
    {
        var force = new Double3(0, 0, -3000);
        Entity own = Shuttle.Park(world, space, new DPose(new Double3(-1000, 0, 0), DQuaternion.Identity), components: [new Unpark(), new PushAt(5, force, own: true)]);
        Entity jets = Shuttle.Park(world, space, new DPose(new Double3(1000, 0, 0), DQuaternion.Identity), components: [new Unpark(), new PushAt(5, force, own: false)]);
        Shuttle.FitJets(world, jets);
        long ownMoves = -1, jetsMove = -1;

        for (int tick = 0; tick < 20; tick++)
        {
            world.Tick();
            if (ownMoves < 0 && world.VelocityIn(own, space).Length > 0)
                ownMoves = world.TickNumber;
            if (jetsMove < 0 && world.VelocityIn(jets, space).Length > 0)
                jetsMove = world.TickNumber;
        }

        ownMoves.Should().BePositive("the hull's own push moves it");
        jetsMove.Should().Be(ownMoves + 1);
    }

    /// <summary>
    /// A throttle outside 0 to 1 is a bug of the sender. The jet's part refuses it with an error and pushes nothing, so the
    /// hull does not move.
    /// </summary>
    [Theory]
    [InlineData(1.5f)]
    [InlineData(-0.5f)]
    [InlineData(float.NaN)]
    public void AJetGivenAThrottleOutsideZeroToOneIsAnErrorAndPushesNothing(float value)
    {
        Launch(DQuaternion.Identity);
        world.AddComponent(shuttle, new SendThrottle(Control.JetParts[ShuttleJetControl.PartIndex(6, false)], new Throttle { Value = value }));

        Tick(world, 30);

        world.VelocityIn(shuttle, space).Length.Should().BeLessThan(1e-6);
        world.Errors.Should().Contain(error => error.Contains(nameof(JetThruster)) && error.Contains("A throttle lies from 0 to 1"));
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

    /// <summary>
    /// A test component that, in each of the <paramref name="count"/> ticks from tick <paramref name="from"/> on, pushes its
    /// own body with <paramref name="torque"/>, when <paramref name="own"/>, or adds a <see cref="Torque"/> of it for the jets.
    /// </summary>
    private sealed class TorqueAt(long from, int count, Double3 torque, bool own) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick < from || self.Tick >= from + count)
                return;
            if (own)
                self.Body.AddTorque(torque.ToVector3());
            else
                events.Add(new Torque { Value = torque });
        }
    }

    /// <summary>A test component that adds a <see cref="Throttle"/> of <paramref name="value"/> to its entity's events in every tick.</summary>
    private sealed class ThrottleFor(float value) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => events.Add(new Throttle { Value = value });
    }

    /// <summary>
    /// A test component that in tick <paramref name="tick"/> pushes its own body with <paramref name="force"/>, when
    /// <paramref name="own"/>, or adds a <see cref="Translation"/> of it for the jets.
    /// </summary>
    private sealed class PushAt(long tick, Double3 force, bool own) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick != tick)
                return;
            if (own)
                self.Body.AddForce(force.ToVector3());
            else
                events.Add(new Translation { Force = force });
        }
    }

    /// <summary>A test component that sends <paramref name="throttle"/> to <paramref name="target"/> in every tick.</summary>
    private sealed class SendThrottle(Entity target, Throttle throttle) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => outbox.Send(target, throttle);
    }
}
