using System.Numerics;
using Tertius.Demo;
using Tertius.Frames.Rails;
using Tertius.Physics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The shuttle's <see cref="RendezvousAutopilot"/>, on the World and the real Jolt backend (docs/plans/blue-danube-demo.md,
/// "The rendezvous autopilot"). The demo's station flies its orbit, 400 km up, in a root frame with Earth's point gravity,
/// and the shuttle flies near it with its flight controls: the autopilot, then the attitude autopilot, the main engine
/// and the translation thrusters of the demo. Between the autopilot and the actuators, a <see cref="Recorder"/> sees what
/// the autopilot adds in each tick. Some tests fly the demo's plan, which waits in the parking orbit, 200 km up, for a
/// transfer of 170 degrees. The others fly <see cref="ShortPlanRun.Plan"/>, a transfer of 300 s from 5 km behind the
/// station, because the demo's plan takes an hour.
/// </summary>
public sealed class RendezvousAutopilotTests : IDisposable, IClassFixture<RendezvousAutopilotTests.ShortPlanRun>
{
    internal const double Mu = DemoScenes.EarthMu;

    /// <summary>The main engine's thrust at full throttle, in newtons, as <see cref="Shuttle.FlightControls"/> gives it.</summary>
    internal const float Thrust = Shuttle.Mass * Shuttle.ThrustAcceleration;

    private readonly ShortPlanRun run;
    private readonly World world = NewWorld();
    private readonly FrameHandle earth;
    private readonly Entity station;
    private readonly Recorder recorder = new();
    private readonly List<string> log = [];
    private Entity shuttle;

    public RendezvousAutopilotTests(ShortPlanRun run)
    {
        this.run = run;
        earth = Earth(world);
        station = DemoStation.Spawn(world, earth, new SceneInstance(), RenderResources.None);
        world.Log += log.Add;
    }

    public void Dispose() => world.Dispose();

    private RendezvousAutopilot Autopilot => world.GetComponent<RendezvousAutopilot>(shuttle)!;

    /// <summary>
    /// A world on the real Jolt backend, which it owns, with one gameplay thread, without the invariant checks of every
    /// tick. With the station, those checks cost 6.6 ms a tick, against 0.125 ms without them (measured on 2026-10-20), and
    /// these tests fly tens of thousands of ticks.
    /// </summary>
    internal static World NewWorld() =>
        new(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = false, DisposePhysics = true }, new JoltPhysicsBackend());

    /// <summary>A root frame with Earth's point gravity, which does not turn: the analogue of <c>earth-surface</c>.</summary>
    internal static FrameHandle Earth(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e8f), GravityField.Point(Mu)));

    /// <summary>
    /// Spawns the shuttle in <paramref name="frame"/> at <paramref name="position"/>, moving at <paramref name="velocity"/>,
    /// with its nose along the velocity and its roof away from Earth, and with the flight controls of the rendezvous to
    /// <paramref name="station"/> on <paramref name="plan"/>. The <paramref name="recorder"/> runs between the autopilot and
    /// the actuators.
    /// </summary>
    internal static Entity Fly(World world, FrameHandle frame, Entity station, RendezvousPlan plan, Double3 position, Double3 velocity, Recorder recorder) =>
        world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = new DPose(position, AttitudeAutopilot.Facing(velocity, position)),
            LinearVelocity = velocity,
            Body = new BodyDesc(new BoxShapeDesc(Shuttle.Hull), Mass: Shuttle.Mass, LinearDamping: 0, AngularDamping: 0,
                InteriorShape: new CompoundShapeDesc([.. Shuttle.Interior().Select(box => new CompoundChild(new BoxShapeDesc(box.Extents), new Pose(box.Center, Quaternion.Identity)))])),
            AsFrame = new FrameSpec(new FrameBounds(Shuttle.Bubble)),
            Components = [new RendezvousAutopilot(station, plan, Thrust), recorder, new AttitudeAutopilot(Shuttle.Inertia), new MainEngine(Thrust),
                new TranslationThrusters(Shuttle.TranslationThrust)],
        });

    /// <summary>The station's center, velocity and axis in <paramref name="frame"/>, as the world reads them now.</summary>
    internal static (Double3 Position, Double3 Velocity, Double3 Axis) StationNow(World world, Entity station, FrameHandle frame)
    {
        DPose pose = world.PoseIn(station, frame);
        return (pose.Position, world.VelocityIn(station, frame), pose.Rotation.Rotate(Double3.UnitY));
    }

    /// <summary>
    /// The state on the parking circle, 200 km up in the station's plane, from which the transfer angle to the demo plan's
    /// arrival point is <paramref name="degrees"/>, at the world's time, from the station's rails.
    /// </summary>
    private (Double3 Position, Double3 Velocity) ParkingAt(double degrees)
    {
        (Double3 place, Double3 velocity) = DemoStation.Orbit.StateAt(world.Time, Mu);
        Double3 axis = DemoStation.Orbit.RotationAt(world.Time, Mu).Rotate(Double3.UnitY);
        Double3 arrival = new TransferGuidance(Mu).Arrival(place, velocity, axis, RendezvousPlan.Demo.ArrivalDistance, RendezvousPlan.Demo.TransferTime).Point;
        Double3 normal = TransferGuidance.OrbitNormal(place, velocity);
        Double3 up = DQuaternion.FromAxisAngle(normal, -degrees * Math.PI / 180).Rotate((arrival - normal * Double3.Dot(arrival, normal)).Normalized());
        return (up * Shuttle.ParkingOrbitRadius, Double3.Cross(normal, up) * Math.Sqrt(Mu / Shuttle.ParkingOrbitRadius));
    }

    /// <summary>Spawns the shuttle on the parking circle at a transfer angle of <paramref name="degrees"/>, on the demo's plan.</summary>
    private void ParkAt(double degrees)
    {
        (Double3 position, Double3 velocity) = ParkingAt(degrees);
        shuttle = Fly(world, earth, station, RendezvousPlan.Demo, position, velocity, recorder);
    }

    /// <summary>
    /// Posts the order, and ticks once, so that the world routes it. The autopilot handles it in the next tick.
    /// </summary>
    private void Order()
    {
        world.Post(Event.Create(shuttle, Entity.Null, new Rendezvous()));
        world.Tick();
    }

    /// <summary>The transfer angle from the shuttle to the demo plan's arrival point, in degrees, as the world reads the states now.</summary>
    private double TransferAngleNow()
    {
        (Double3 place, Double3 velocity, Double3 axis) = StationNow(world, station, earth);
        Double3 arrival = new TransferGuidance(Mu).Arrival(place, velocity, axis, RendezvousPlan.Demo.ArrivalDistance, RendezvousPlan.Demo.TransferTime).Point;
        return TransferGuidance.TransferAngle(world.PoseIn(shuttle, earth).Position, arrival, TransferGuidance.OrbitNormal(place, velocity)) * 180 / Math.PI;
    }

    /// <summary>The steering of the transfer now, from the states that the world reads, for the arrival time of the autopilot's transfer.</summary>
    private TransferSteering SteeringNow(Transfer transfer)
    {
        (Double3 place, Double3 velocity, Double3 axis) = StationNow(world, station, earth);
        return transfer.Guidance.Steer(world.PoseIn(shuttle, earth).Position, world.VelocityIn(shuttle, earth), place, velocity, axis,
            RendezvousPlan.Demo.ArrivalDistance, transfer.ArrivalTime - world.Time);
    }

    /// <summary>
    /// The shuttle waits in the parking orbit inside the window for 5 s, but no order comes. The autopilot stays in
    /// <see cref="Standby"/>, and adds no throttle, no attitude target and no translation.
    /// </summary>
    [Fact]
    public void WithoutAnOrderTheAutopilotAddsNothing()
    {
        ParkAt(160);

        Tick(world, 300);

        Autopilot.State.Value.Should().BeOfType<Standby>();
        recorder.TicksWithEvents.Should().Be(0);
    }

    /// <summary>
    /// The order comes at a transfer angle of 170.1 degrees, before the window. The shuttle gains on the station at about
    /// 0.003 degrees a second, so the angle falls to 170 degrees in about 33 s. Until then the autopilot waits in
    /// <see cref="Phasing"/> and adds nothing. It starts the transfer in the tick where the angle reaches 170 degrees.
    /// </summary>
    [Fact]
    public void AfterTheOrderTheAutopilotWaitsUntilTheTransferAngleFallsTo170Degrees()
    {
        ParkAt(170.1);
        Order();

        int waited = TickUntil(world, () => Autopilot.State.Value is not (Standby or Phasing), limit: 60 * 60);

        Autopilot.State.Value.Should().BeOfType<Transfer>();
        waited.Should().BeGreaterThan(30 * 60, "the angle falls by 0.1 degrees in about 33 s");
        TransferAngleNow().Should().BeApproximately(170, 0.001, "the transfer starts where the angle reaches 170 degrees");
        recorder.TicksWithEvents.Should().Be(1, "the autopilot added nothing while it waited, and burns in the tick of the window");
    }

    /// <summary>
    /// The order comes at a transfer angle of 160 degrees, inside the window. In the tick of the order, the autopilot
    /// starts the transfer, with the arrival time one transfer time later, and burns.
    /// </summary>
    [Fact]
    public void AnOrderInsideTheWindowStartsTheTransferAtOnce()
    {
        ParkAt(160);
        Order();
        double time = world.TimeAt(world.TickNumber);

        world.Tick();

        Autopilot.State.Value.Should().Be(new Transfer(new TransferGuidance(Mu), time + RendezvousPlan.Demo.TransferTime));
        recorder.Throttle.Should().BeGreaterThan(0, "the burn starts in the tick of the order");
    }

    /// <summary>
    /// The order comes at the window, on the demo's plan. The shuttle flies the burn, until the main engine stops, and
    /// then 20 s of the trim. The two-body path of the state that it reaches then passes within 100 m of the arrival point
    /// at the arrival time.
    /// </summary>
    [Fact]
    public void TheBurnPutsTheShuttleOnATwoBodyPathThatReachesTheArrivalPointAtTheArrivalTime()
    {
        ParkAt(169.99);
        Order();
        world.Tick();
        Transfer transfer = (Transfer)Autopilot.State.Value;

        int burn = TickUntil(world, () => recorder.Throttle is null, limit: 60 * 60);
        Tick(world, 20 * 60);

        burn.Should().BeInRange(15 * 60, 50 * 60, "a burn of about 60 m/s at 3 m/s² with its trim in the last seconds");
        Autopilot.State.Value.Should().Be(transfer);
        double timeLeft = transfer.ArrivalTime - world.Time;
        (Double3 landing, _) = KeplerOrbit.Propagate(world.PoseIn(shuttle, earth).Position, world.VelocityIn(shuttle, earth), Mu, timeLeft);
        (Double3 place, Double3 velocity, Double3 axis) = StationNow(world, station, earth);
        Double3 arrival = transfer.Guidance.Arrival(place, velocity, axis, RendezvousPlan.Demo.ArrivalDistance, timeLeft).Point;
        Double3.Distance(landing, arrival).Should().BeLessThan(100);
    }

    /// <summary>
    /// The burn ends in the first tick that adds no throttle. The velocity to gain is then within
    /// <see cref="TransferGuidance.SpeedTolerance"/>, 0.1 m/s, but not far within it, and the last throttle pushed the shuttle
    /// with about half the weightless threshold, <see cref="FrameTree.ZeroGravityThreshold"/>.
    /// </summary>
    [Fact]
    public void TheBurnEndsAtTheSpeedThatTheTrimTakesAtHalfTheWeightlessThreshold()
    {
        ParkAt(169.99);
        Order();
        world.Tick();
        Transfer transfer = (Transfer)Autopilot.State.Value;
        float last = 0;

        TickUntil(world, () =>
        {
            if (recorder.Throttle is not float throttle)
                return true;
            last = throttle;
            return false;
        }, limit: 60 * 60);

        recorder.Throttle.Should().BeNull("the burn ended within a minute");
        SteeringNow(transfer).ToGain.Length.Should().BeInRange(0.075, TransferGuidance.SpeedTolerance);
        ((double)last * Thrust / Shuttle.Mass).Should().BeLessThan(FrameTree.ZeroGravityThreshold / 2 * 1.2,
            "the thrust of the burn falls to half the weightless threshold at its end");
    }

    /// <summary>
    /// After the burn, the nose turns from the burn's direction to the brake's, about 160 degrees, while the cabin is
    /// weightless. So in the first 300 s of the trim the hull turns no faster than
    /// the demo plan's <see cref="RendezvousPlan.WeightlessTurnRate"/>, with 5 percent for the attitude autopilot's lag, and it does turn.
    /// </summary>
    [Fact]
    public void InTheTrimTheNoseTurnsNoFasterThanTheWeightlessTurnRate()
    {
        ParkAt(169.99);
        Order();
        world.Tick();
        recorder.Throttle.Should().NotBeNull("the burn starts in the tick after the order");
        TickUntil(world, () => recorder.Throttle is null, limit: 60 * 60);
        double fastest = 0;

        for (int tick = 0; tick < 300 * 60; tick++)
        {
            world.Tick();
            fastest = Math.Max(fastest, world.AngularVelocityIn(shuttle, earth).Length());
        }

        fastest.Should().BeInRange(RendezvousPlan.Demo.WeightlessTurnRate * 0.9, RendezvousPlan.Demo.WeightlessTurnRate * 1.05);
    }

    /// <summary>
    /// A plan whose weightless turn rate is not above 0, or is faster than the attitude autopilot turns, is refused at the
    /// autopilot's construction.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(AttitudeAutopilot.MaxTurnRate * 1.01)]
    public void APlanWithAWeightlessTurnRateOutsideTheAttitudeAutopilotsRangeIsRefused(double rate)
    {
        Action build = () => new RendezvousAutopilot(station, RendezvousPlan.Demo with { WeightlessTurnRate = rate }, Thrust);

        build.Should().Throw<ArgumentException>().WithParameterName("plan");
    }

    /// <summary>
    /// On the short plan, the brake ends within 10 m of the arrival point and slower than 0.5 m/s relative to the station,
    /// within a minute after the arrival time. At the arrival time, the shuttle is within 100 m of the arrival point.
    /// </summary>
    [Fact]
    public void TheBrakeStopsTheShuttleAtTheArrivalPoint()
    {
        ShortPlanRun.Record[] brake = [.. run.Log.Where(record => record.State is Brake)];

        brake.Should().NotBeEmpty("the shuttle brakes");
        ShortPlanRun.Record end = brake[^1];
        end.FromArrival.Should().BeLessThan(10);
        end.RelativeSpeed.Should().BeLessThan(0.5);
        end.Time.Should().BeLessThan(run.ArrivalTime + 60);
        run.Log.First(record => record.Time >= run.ArrivalTime).FromArrival.Should().BeLessThan(100, "the miss at the arrival time");
    }

    /// <summary>
    /// On the short plan, the glide flies the shuttle from the arrival point, 400 m out on the station's axis, to the hold
    /// point, 150 m out. In every tick of the glide, the shuttle stays within 5 m of the axis, and the glide ends within
    /// 0.5 m of the hold point.
    /// </summary>
    [Fact]
    public void TheGlideFliesTheShuttleAlongTheAxisToTheHoldPoint()
    {
        ShortPlanRun.Record[] glide = [.. run.Log.Where(record => record.State is Glide)];

        glide.Should().NotBeEmpty("the shuttle glides");
        glide.Max(record => record.FromAxis).Should().BeLessThan(5);
        run.Log.Should().Contain(record => record.State is Hold, "the glide ends at the hold");
        run.Log.First(record => record.State is Hold).FromHold.Should().BeLessThan(0.5);
    }

    /// <summary>
    /// On the short plan, for 60 s after the glide ends, the shuttle stays within 0.5 m of the hold point, slower than
    /// 0.02 m/s relative to the station, with its nose within 1 degree of minus the axis, at the slot.
    /// </summary>
    [Fact]
    public void TheShuttleHoldsAtTheHoldPointWithItsNoseAtTheSlot()
    {
        ShortPlanRun.Record[] hold = [.. run.Log.SkipWhile(record => record.State is not Hold)];

        hold.Should().HaveCountGreaterThanOrEqualTo(60 * 60);
        hold.Should().AllSatisfy(record => record.State.Should().BeOfType<Hold>());
        hold.Max(record => record.FromHold).Should().BeLessThan(0.5);
        hold.Max(record => record.RelativeSpeed).Should().BeLessThan(0.02);
        hold.Max(record => record.NoseFromSlot).Should().BeLessThan(1);
    }

    /// <summary>
    /// The station is destroyed while the autopilot waits for the window. In the next tick the autopilot halts, and it
    /// logs the reason once.
    /// </summary>
    [Fact]
    public void TheAutopilotStopsWhenTheStationIsGone()
    {
        ParkAt(175);
        Order();
        Tick(world, 10);
        Autopilot.State.Value.Should().BeOfType<Phasing>();

        world.Destroy(station);
        Tick(world, 10);

        Autopilot.State.Value.Should().BeOfType<Halted>();
        log.Count(line => line.Contains(nameof(RendezvousAutopilot))).Should().Be(1);
        recorder.TicksWithEvents.Should().Be(0);
    }

    /// <summary>
    /// The shuttle flies near the station in a root frame with no point gravity. The order makes the autopilot halt at
    /// once, because it has no G·M to fly by, and it logs the reason once.
    /// </summary>
    [Fact]
    public void TheAutopilotStopsWhenItsFrameHasNoPointGravity()
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Entity other = world.Spawn(new SpawnDesc { Frame = space, Pose = DPose.Identity, Body = Cube() });
        shuttle = Fly(world, space, other, RendezvousPlan.Demo, new Double3(0, 0, 100), Double3.UnitX, recorder);
        Order();

        Tick(world, 10);

        Autopilot.State.Value.Should().BeOfType<Halted>();
        log.Count(line => line.Contains(nameof(RendezvousAutopilot))).Should().Be(1);
        recorder.TicksWithEvents.Should().Be(0);
    }

    /// <summary>
    /// A test component that runs between the rendezvous autopilot, at priority 1, and the actuators, at 10. It counts the
    /// ticks in which the events hold a <see cref="Throttle"/>, an <see cref="AttitudeTarget"/> or a
    /// <see cref="Translation"/>, and keeps the last throttle of the last tick, or null when that tick had none.
    /// </summary>
    internal sealed class Recorder : IComponent
    {
        public int TicksWithEvents { get; private set; }

        public float? Throttle { get; private set; }

        public int Priority => 5;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            Throttle = null;
            foreach (ref Throttle each in events.Of<Throttle>())
                Throttle = each.Value;
            if (Throttle is not null || events.Of<AttitudeTarget>().Any() || events.Of<Translation>().Any())
                TicksWithEvents++;
        }
    }

    /// <summary>
    /// One flight of the short plan, shared by the tests of the brake, the glide and the hold: the station on its orbit,
    /// and the shuttle 5 km behind it on the same circle. The order comes in the second tick, inside the plan's window, and
    /// the flight runs until the shuttle has held for 60 s, or for 20 minutes at most. The log keeps a record of every tick.
    /// </summary>
    public sealed class ShortPlanRun : IDisposable
    {
        /// <summary>
        /// The short plan: a transfer of 300 s to an arrival point 400 m out on the axis, and the demo's hold point, 150 m
        /// out. From 5 km behind the station, the transfer angle is about 19.5 degrees, inside the window from 30 down to 10
        /// degrees. The shuttle carries no crew, and the turn to the brake's direction must end in the 300 s, so the nose
        /// turns at the attitude autopilot's full rate in the trim and the coast too.
        /// </summary>
        public static RendezvousPlan Plan { get; } = new(TransferTime: 300, ArrivalDistance: 400, HoldDistance: DemoStation.HoldPoint.Y,
            WindowOpens: 30 * Math.PI / 180, WindowCloses: 10 * Math.PI / 180, WeightlessTurnRate: AttitudeAutopilot.MaxTurnRate);

        /// <summary>The state of the autopilot after a tick, and the shuttle's state relative to the station then.</summary>
        /// <param name="Time">The world's time after the tick, in seconds.</param>
        /// <param name="State">The autopilot's state after the tick.</param>
        /// <param name="FromArrival">The distance from the shuttle's center to the arrival point, in meters.</param>
        /// <param name="FromHold">The distance from the shuttle's center to the hold point, in meters.</param>
        /// <param name="FromAxis">The distance from the shuttle's center to the station's axis, in meters.</param>
        /// <param name="RelativeSpeed">The shuttle's speed relative to the station, in m/s.</param>
        /// <param name="NoseFromSlot">The angle between the shuttle's nose and minus the station's axis, in degrees.</param>
        public readonly record struct Record(double Time, object State, double FromArrival, double FromHold, double FromAxis, double RelativeSpeed, double NoseFromSlot);

        private readonly World world = NewWorld();

        public ShortPlanRun()
        {
            FrameHandle earth = Earth(world);
            Entity station = DemoStation.Spawn(world, earth, new SceneInstance(), RenderResources.None);
            KeplerOrbit orbit = DemoStation.Orbit;
            (Double3 position, Double3 velocity) = (orbit with { MeanAnomalyAtEpoch = orbit.MeanAnomalyAtEpoch - 5000 / orbit.SemiMajorAxis }).StateAt(world.Time, Mu);
            Entity shuttle = Fly(world, earth, station, Plan, position, velocity, new Recorder());
            RendezvousAutopilot autopilot = world.GetComponent<RendezvousAutopilot>(shuttle)!;
            world.Post(Event.Create(shuttle, Entity.Null, new Rendezvous()));
            Tick(world, 2);
            ArrivalTime = autopilot.State.TryGetValue(out Transfer transfer) ? transfer.ArrivalTime : double.NaN;

            int held = 0;
            for (int tick = 0; tick < 20 * 60 * 60 && held < 60 * 60; tick++)
            {
                world.Tick();
                (Double3 center, Double3 stationVelocity, Double3 axis) = StationNow(world, station, earth);
                DPose pose = world.PoseIn(shuttle, earth);
                Double3 offset = pose.Position - center;
                Double3 nose = pose.Rotation.Rotate(-Double3.UnitZ);
                Log.Add(new Record(world.Time, autopilot.State.Value,
                    FromArrival: Double3.Distance(offset, axis * Plan.ArrivalDistance),
                    FromHold: Double3.Distance(offset, axis * Plan.HoldDistance),
                    FromAxis: (offset - axis * Double3.Dot(offset, axis)).Length,
                    RelativeSpeed: (world.VelocityIn(shuttle, earth) - stationVelocity).Length,
                    NoseFromSlot: Math.Acos(Math.Clamp(Double3.Dot(nose, -axis), -1, 1)) * 180 / Math.PI));
                if (autopilot.State.Value is Hold)
                    held++;
            }
        }

        /// <summary>The arrival time of the transfer that the order started, in the world's seconds, or NaN when it started none.</summary>
        public double ArrivalTime { get; }

        /// <summary>The records of the ticks after the order, oldest first.</summary>
        public List<Record> Log { get; } = [];

        public void Dispose() => world.Dispose();
    }
}
