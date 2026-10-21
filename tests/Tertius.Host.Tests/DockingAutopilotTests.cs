using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;
using SpinUp = Tertius.Demo.SpinUp;

namespace Tertius.Host.Tests;

/// <summary>
/// The shuttle's <see cref="DockingAutopilot"/>, on the World and the real Jolt backend (docs/plans/blue-danube-demo.md,
/// "The docking"). The demo's station flies its orbit in a root frame with Earth's point gravity, as in
/// <see cref="RendezvousAutopilotTests"/>, and turns at 0.31321 rad/s. The shuttle starts at the hold point, 150 m out on
/// the station's axis, at rest relative to the station, with the hold rotation: its nose toward the slot along minus the
/// axis, its roof away from Earth, and still among the stars. It has the docking autopilot and the actuators, and no
/// rendezvous, so the tests run in seconds, not from the launch. Between the autopilot and the actuators, a
/// <see cref="RendezvousAutopilotTests.Recorder"/> sees what the autopilot adds in each tick. Most tests read one shared
/// flight, <see cref="DockingFlight"/>. Every place is in the station's axes.
/// </summary>
public sealed class DockingAutopilotTests(DockingAutopilotTests.DockingFlight flight) : IClassFixture<DockingAutopilotTests.DockingFlight>
{
    private const double Rate = DemoScenes.SpinStation.Rate, Tick = 1.0 / 60, Degree = Math.PI / 180;

    private IReadOnlyList<DockingFlight.Record> Log => flight.Log;

    private DockingFlight.Record FirstOf<T>() => Log.First(record => record.State.Value is T);

    private int IndexOfFirst<T>() => Log.ToList().FindIndex(record => record.State.Value is T);

    private static double AngleBetween(DQuaternion a, DQuaternion b) => a.AngularVelocityTo(b, 1).Length;

    /// <summary>
    /// The shuttle holds at the hold point for 5 s, but no order comes. The docking autopilot stays in <see cref="Standby"/>,
    /// and adds no attitude target, no translation and no throttle.
    /// </summary>
    [Fact]
    public void WithoutAnOrderTheDockingAddsNothing()
    {
        using World world = RendezvousAutopilotTests.NewWorld();
        (Entity station, Entity shuttle, var recorder) = DockingFlight.AtTheHold(world);

        Tick(world, 300);

        world.GetComponent<DockingAutopilot>(shuttle)!.State.Value.Should().BeOfType<Standby>();
        recorder.TicksWithEvents.Should().Be(0);
        world.IsAlive(station).Should().BeTrue();
    }

    /// <summary>
    /// After the order the autopilot waits and adds nothing, until the station's turn brings the roll angle into its
    /// window. That takes at most one turn of the station, 20.1 s. The tick that starts the spin-up read a roll angle within
    /// half a tick's turn of zero, ω dt / 2.
    /// </summary>
    [Fact]
    public void TheSpinUpStartsWithinOneTurnOfTheStationAtTheRollWindow()
    {
        int start = IndexOfFirst<SpinUp>();

        start.Should().BePositive("the spin-up starts");
        (Log[start].Time - flight.OrderTime).Should().BeLessThanOrEqualTo(2 * Math.PI / Rate + Tick);
        Log.Take(start).All(record => record.State.Value is Standby or Waiting).Should().BeTrue("the docking waits until the start");
        Log.Take(start).Should().OnlyContain(record => !record.Added, "the docking adds nothing while it waits");
        var profile = new SpinUpProfile(Rate, DockingAutopilot.SpinUpTime, DemoStation.DockedRotation);
        Math.Abs(profile.RollAngle(Log[start - 1].Pose.Rotation)).Should().BeLessThanOrEqualTo(Rate * Tick / 2 + 1e-4,
            "the tick of the start read the roll angle inside the window");
    }

    /// <summary>
    /// The spin-up turns the hull with a constant angular acceleration of ω / T, 0.0104 rad/s², about its long axis. At
    /// the seat stand, 1.35 m from that axis, the Euler term of the turn is about 0.014 m/s². In every tick of the
    /// spin-up it stays below half the weightless threshold, so the turn's start and end give the strapped person no jolt.
    /// </summary>
    [Fact]
    public void TheSpinUpNeverGivesTheSeatAnEulerTermAboveHalfTheWeightlessThreshold()
    {
        DockingFlight.Record[] spinUp = [.. Log.Where(record => record.State.Value is SpinUp)];

        spinUp.Should().HaveCountGreaterThan((int)(DockingAutopilot.SpinUpTime * 60) - 1);
        spinUp.Max(record => record.SeatEuler).Should().BeLessThan(FrameTree.ZeroGravityThreshold / 2);
        spinUp.Max(record => record.SeatEuler).Should().BeGreaterThan(0.005, "the turn does accelerate");
    }

    /// <summary>
    /// At the end of the spin-up, in the first tick of the entry, the shuttle turns with the station, slower than 0.005
    /// rad/s relative to it, within 1 degree of the docked rotation, belly toward the deck, and within 0.5 m of the hold
    /// point. The spin-up took its 30 s.
    /// </summary>
    [Fact]
    public void TheSpinUpEndsTurningWithTheStationWithTheBellyTowardTheDeck()
    {
        DockingFlight.Record entry = FirstOf<Entry>(), start = FirstOf<SpinUp>();

        (entry.Time - start.Time).Should().BeInRange(DockingAutopilot.SpinUpTime, DockingAutopilot.SpinUpTime + 2);
        entry.AngularVelocity.Length.Should().BeLessThan(0.005);
        AngleBetween(entry.Pose.Rotation, DemoStation.DockedRotation).Should().BeLessThan(Degree);
        (entry.Pose.Position - DemoStation.HoldPoint).Length.Should().BeLessThan(0.5);
    }

    /// <summary>
    /// The shuttle enters the station's frame where its bounding sphere meets the station's bubble, about 128.8 m from the
    /// station's center. Across the hand-off, its pose and velocity in the station's frame go on within 1 mm and 1 mm/s of
    /// where its motion led, and its cabin frame becomes a child of the station's frame. If this fails, the failure is an
    /// engine bug for its own ticket, which goes to the user.
    /// </summary>
    [Fact]
    public void TheShuttleEntersTheStationsFrameAtItsBubbleAndKeepsItsMotion()
    {
        int inside = Log.ToList().FindIndex(record => record.Frame == flight.StationFrame);

        inside.Should().BePositive("the shuttle enters the station's frame");
        DockingFlight.Record before = Log[inside - 1], after = Log[inside];
        before.Frame.Should().Be(flight.Earth);
        before.Pose.Position.Length.Should().BeInRange(128.5, 129.5, "the bounding sphere meets the bubble there");
        (after.Pose.Position - (before.Pose.Position + before.Velocity * Tick)).Length.Should().BeLessThan(1e-3);
        (after.Velocity - before.Velocity).Length.Should().BeLessThan(1e-3);
        Log.Skip(inside).Should().OnlyContain(record => record.Frame == flight.StationFrame && record.CabinParent == flight.StationFrame);
    }

    /// <summary>
    /// While any part of the hull lies within the slot's depth, from y = 12.5 to 13 m, the hull's origin lies within 0.25 m
    /// of the axis and its rotation within 1 degree of the docked rotation, and every corner of the hull keeps 0.5 m from
    /// the slot's edges, which lie 1 m from the hull's sides in the docked rotation. No contact starts before the move out.
    /// </summary>
    [Fact]
    public void TheHullPassesTheSlotWithinHalfItsClearance()
    {
        BoundingBox slot = DemoStation.Slot;
        DockingFlight.Record[] inTheSlot = [.. Log.Where(record => record.HullMinY <= slot.Max.Y && record.HullMaxY >= slot.Min.Y)];

        inTheSlot.Should().HaveCountGreaterThan(60 * 5, "the hull takes several seconds through the slot");
        foreach (DockingFlight.Record record in inTheSlot)
        {
            Math.Sqrt(record.Pose.Position.X * record.Pose.Position.X + record.Pose.Position.Z * record.Pose.Position.Z)
                .Should().BeLessThan(0.25, $"the origin keeps to the axis at {record.Time:F2} s");
            AngleBetween(record.Pose.Rotation, DemoStation.DockedRotation).Should().BeLessThan(Degree, $"the hull keeps the docked rotation at {record.Time:F2} s");
            record.HullMaxAbsX.Should().BeLessThanOrEqualTo(slot.Max.X - 0.5, $"the hull keeps clear of the slot's edges along X at {record.Time:F2} s");
            record.HullMaxAbsZ.Should().BeLessThanOrEqualTo(slot.Max.Z - 0.5, $"the hull keeps clear of the slot's edges along Z at {record.Time:F2} s");
        }
        flight.FirstContactTime.Should().BeGreaterThan(FirstOf<MoveOut>().Time, "nothing touches the hull before the move out");
    }

    /// <summary>
    /// In every tick of the entry, the hull's origin lies within 0.1 m of the station's axis. The law holds it there
    /// because it cancels the spin's terms at the center of mass, 9 cm below the origin, where they act on the hull. A law
    /// that cancels them at the origin leaves 9 mm/s² of the centrifugal term, which the wanted velocity toward the far
    /// entry's end hardly resists, and the hull drifts 0.33 m from the axis (measured on 2026-10-20).
    /// </summary>
    [Fact]
    public void TheEntryKeepsTheOriginOnTheAxis()
    {
        DockingFlight.Record[] entry = [.. Log.Where(record => record.State.Value is Entry)];

        entry.Should().HaveCountGreaterThan(60 * 60, "the entry takes about 90 s");
        entry.Max(record => Math.Sqrt(record.Pose.Position.X * record.Pose.Position.X + record.Pose.Position.Z * record.Pose.Position.Z)).Should().BeLessThan(0.1);
    }

    /// <summary>
    /// The move out flies the hull on the straight line from where it starts, within 0.5 m of the entry's end, to the
    /// landing point, fixed in the station's axes. Until the first contact, its origin stays within 0.3 m of that line, so
    /// the law cancels the centrifugal and Coriolis terms that grow as it moves out.
    /// </summary>
    [Fact]
    public void TheMoveOutKeepsTheHullOnItsCoRotatingPath()
    {
        DockingFlight.Record[] moveOut = [.. Log.Where(record => record.State.Value is MoveOut && record.Time < flight.FirstContactTime)];

        moveOut.Should().HaveCountGreaterThan(60 * 10, "the move out takes about half a minute");
        Double3 from = moveOut[0].Pose.Position, to = DockingAutopilot.LandingPoint;
        (from - DockingAutopilot.EntryEnd).Length.Should().BeLessThanOrEqualTo(DockingAutopilot.EntryEndDistance * Math.Sqrt(2));
        foreach (DockingFlight.Record record in moveOut)
        {
            Double3 offset = record.Pose.Position - from, line = (to - from).Normalized();
            double along = Math.Clamp(Double3.Dot(offset, line), 0, (to - from).Length);
            (offset - line * along).Length.Should().BeLessThan(0.3, $"the hull keeps its path at {record.Time:F2} s");
        }
    }

    /// <summary>
    /// The move out ends at rest in the corner of the deck and the bay's -Z wall: within 3 cm of the parked point across
    /// the axis, 0.1 m along it and 0.5 degrees of the docked rotation. The autopilot makes the hull static in that tick, so
    /// from the next step on physics holds it static in the station's frame.
    /// </summary>
    [Fact]
    public void TheShuttleLandsAtTheParkedPoseAndIsMadeStatic()
    {
        int docked = IndexOfFirst<Docked>();

        docked.Should().BePositive("the shuttle docks");
        DockingFlight.Record landed = Log[docked];
        Double3 off = landed.Pose.Position - DemoStation.ParkedPose.Position;
        Math.Abs(off.X).Should().BeLessThan(0.03);
        Math.Abs(off.Z).Should().BeLessThan(0.03);
        Math.Abs(off.Y).Should().BeLessThan(0.1);
        AngleBetween(landed.Pose.Rotation, DemoStation.DockedRotation).Should().BeLessThan(0.5 * Degree);
        Log[docked + 3].Motion.Should().Be(BodyMotion.Static);
        Log[docked + 3].Frame.Should().Be(flight.StationFrame);
    }

    /// <summary>
    /// For the 10 s after the docking, the shuttle stays exactly where it landed, the docking autopilot stays in
    /// <see cref="Docked"/> and adds nothing, and the world logs no error.
    /// </summary>
    [Fact]
    public void TheDockedShuttleStaysAndTheDockingAddsNothing()
    {
        int docked = IndexOfFirst<Docked>();
        DockingFlight.Record[] after = [.. Log.Skip(docked + 3)];

        after.Should().HaveCountGreaterThanOrEqualTo(60 * 10 - 3);
        after.All(record => record.State.Value is Docked && !record.Added).Should().BeTrue("the docked autopilot adds nothing");
        after.Max(record => (record.Pose.Position - after[0].Pose.Position).Length).Should().BeLessThan(1e-6);
        flight.Errors.Should().BeEmpty();
        flight.Logged.Should().BeEmpty("the docking flies without a halt");
    }

    /// <summary>
    /// The docked cabin feels the spin gravity of its radius: at the seat stand, ω² times its distance from the station's
    /// axis, 0.783 m/s², leaning 30 degrees from the floor's down toward port.
    /// </summary>
    [Fact]
    public void TheDockedCabinFeelsTheWeakGravityOfItsRadius()
    {
        DockingFlight.Record last = Log[^1];
        Double3 seat = last.Pose.TransformPoint(Shuttle.SeatStand);
        double fromTheAxis = Math.Sqrt(seat.X * seat.X + seat.Z * seat.Z);

        last.SeatGravity.Length.Should().BeApproximately(Rate * Rate * fromTheAxis, 0.01 * Rate * Rate * fromTheAxis);
        last.SeatGravity.Length.Should().BeApproximately(0.783, 0.01);
        (Math.Acos(Double3.Dot(last.SeatGravity, -Double3.UnitY) / last.SeatGravity.Length) / Degree).Should().BeApproximately(30, 2);
        last.SeatGravity.X.Should().BeNegative("the gravity leans toward port");
    }

    /// <summary>
    /// The station is destroyed while the autopilot waits for the roll window. In the next tick the autopilot halts, logs
    /// the reason once, and adds nothing.
    /// </summary>
    [Fact]
    public void TheDockingStopsWhenTheStationIsGone()
    {
        using World world = RendezvousAutopilotTests.NewWorld();
        var log = new List<string>();
        world.Log += log.Add;
        (Entity station, Entity shuttle, var recorder) = DockingFlight.AtTheHold(world);
        world.Post(Event.Create(shuttle, Entity.Null, new Dock()));
        Tick(world, 2);
        DockingAutopilot autopilot = world.GetComponent<DockingAutopilot>(shuttle)!;
        autopilot.State.Value.Should().BeOfType<Waiting>();

        world.Destroy(station);
        Tick(world, 10);

        autopilot.State.Value.Should().BeOfType<Halted>();
        log.Count(line => line.Contains(nameof(DockingAutopilot))).Should().Be(1);
        recorder.TicksWithEvents.Should().Be(0);
    }

    /// <summary>
    /// One docking flight, shared by the tests of the spin-up, the entry, the move out and the landing: the shuttle at the
    /// hold point, the order in its first tick, and the flight until 10 s after the docking, or 5 minutes at most. The log
    /// keeps a record of every tick after the order. It runs without the world's invariant checks, which cost 6.6 ms a
    /// tick with the station.
    /// </summary>
    public sealed class DockingFlight : IDisposable
    {
        /// <summary>The state of the autopilot after a tick, and the shuttle's state in the station's frame then.</summary>
        /// <param name="Time">The world's time after the tick, in seconds.</param>
        /// <param name="State">The docking autopilot's state after the tick.</param>
        /// <param name="Added">True when the autopilot added an attitude target, a translation or a throttle in the tick.</param>
        /// <param name="Frame">The shuttle's frame after the tick.</param>
        /// <param name="CabinParent">The parent of the shuttle's own frame, the cabin's.</param>
        /// <param name="Motion">The hull's motion, as the frame tree stores it for the last finished step.</param>
        /// <param name="Pose">The shuttle's pose in the station's frame.</param>
        /// <param name="Velocity">The shuttle's velocity in the station's frame, in m/s.</param>
        /// <param name="AngularVelocity">The shuttle's angular velocity relative to the station, in rad/s, in the station's axes.</param>
        /// <param name="SeatEuler">The Euler term of the hull's turn among the stars at the seat stand, |α × r|, in m/s², from the change of the angular velocity in the tick.</param>
        /// <param name="SeatGravity">The felt gravity at the seat stand, in m/s², in the cabin's axes.</param>
        /// <param name="HullMinY">The lowest y of the hull's corners, in meters.</param>
        /// <param name="HullMaxY">The highest y of the hull's corners, in meters.</param>
        /// <param name="HullMaxAbsX">The farthest of the hull's corners from the axis along X, in meters.</param>
        /// <param name="HullMaxAbsZ">The farthest of the hull's corners from the axis along Z, in meters.</param>
        public readonly record struct Record(double Time, DockingState State, bool Added, FrameHandle Frame, FrameHandle CabinParent, BodyMotion? Motion,
            DPose Pose, Double3 Velocity, Double3 AngularVelocity, double SeatEuler, Double3 SeatGravity,
            double HullMinY, double HullMaxY, double HullMaxAbsX, double HullMaxAbsZ);

        private readonly World world = RendezvousAutopilotTests.NewWorld();

        /// <summary>
        /// Spawns the demo station in a root frame with Earth's point gravity, and the shuttle at the hold point at rest
        /// relative to the station, with the hold rotation and still among the stars, with the docking autopilot, a
        /// recorder and the actuators. Returns the station, the shuttle and the recorder.
        /// </summary>
        internal static (Entity Station, Entity Shuttle, RendezvousAutopilotTests.Recorder Recorder) AtTheHold(World world, IComponent? watcher = null)
        {
            FrameHandle earth = RendezvousAutopilotTests.Earth(world);
            Entity station = DemoStation.Spawn(world, earth, new SceneInstance(), RenderResources.None);
            (Double3 center, Double3 velocity, Double3 axis) = RendezvousAutopilotTests.StationNow(world, station, earth);
            Double3 hold = center + axis * DemoStation.HoldPoint.Y;
            var recorder = new RendezvousAutopilotTests.Recorder();
            Entity shuttle = world.Spawn(new SpawnDesc
            {
                Frame = earth,
                Pose = new DPose(hold, AttitudeAutopilot.Facing(-axis, hold)),
                LinearVelocity = velocity,
                Body = Shuttle.Body(BodyMotion.Dynamic),
                AsFrame = new FrameSpec(new FrameBounds(Shuttle.Bubble)),
                Components = [new DockingAutopilot(station, Shuttle.CenterOfMass), recorder, new AttitudeAutopilot(Shuttle.Inertia),
                    new MainEngine(Shuttle.Mass * Shuttle.ThrustAcceleration), new TranslationThrusters(Shuttle.TranslationThrust),
                    .. watcher is null ? Array.Empty<IComponent>() : [watcher]],
            });
            return (station, shuttle, recorder);
        }

        public DockingFlight()
        {
            world.Log += Logged.Add;
            var contacts = new FirstContact();
            (Entity station, Entity shuttle, var recorder) = AtTheHold(world, contacts);
            Earth = world.GetFrame(shuttle);
            StationFrame = world.GetOwnedFrame(station);
            FrameHandle cabin = world.GetOwnedFrame(shuttle);
            DockingAutopilot autopilot = world.GetComponent<DockingAutopilot>(shuttle)!;
            Double3[] corners = [.. ((ConvexHullShapeDesc)Shuttle.HullShape).Points.Select(point => (Double3)point)];
            world.Post(Event.Create(shuttle, Entity.Null, new Dock()));
            OrderTime = world.Time;

            Double3 lastSpin = (Double3)world.AngularVelocityIn(shuttle, Earth);
            int docked = 0;
            for (int tick = 0; tick < 5 * 60 * 60 && docked < 10 * 60; tick++)
            {
                int before = recorder.TicksWithEvents;
                world.Tick();
                DPose pose = world.PoseIn(shuttle, StationFrame);
                // The turn among the stars: the earth frame does not turn, so the angular velocity in it is the inertial one.
                DPose inEarth = world.PoseIn(shuttle, Earth);
                Double3 spin = (Double3)world.AngularVelocityIn(shuttle, Earth);
                Double3 seat = inEarth.Rotation.Rotate(Shuttle.SeatStand);
                double euler = Double3.Cross((spin - lastSpin) / world.DeltaTime, seat).Length;
                lastSpin = spin;
                Double3[] hull = [.. corners.Select(pose.TransformPoint)];
                Log.Add(new Record(world.Time, autopilot.State, recorder.TicksWithEvents > before, world.GetFrame(shuttle), world.Frames.Parent(cabin),
                    world.Frames.HullMotion(cabin), pose, world.VelocityIn(shuttle, StationFrame), (Double3)world.AngularVelocityIn(shuttle, StationFrame),
                    euler, world.Frames.FeltGravity(cabin, Shuttle.SeatStand),
                    hull.Min(c => c.Y), hull.Max(c => c.Y), hull.Max(c => Math.Abs(c.X)), hull.Max(c => Math.Abs(c.Z))));
                if (autopilot.State.Value is Docked)
                    docked++;
            }
            FirstContactTime = contacts.Time;
            Errors = [.. world.Errors];
        }

        /// <summary>The root frame with Earth's point gravity, which holds the station.</summary>
        public FrameHandle Earth { get; }

        /// <summary>The station's frame.</summary>
        public FrameHandle StationFrame { get; }

        /// <summary>The world's time at which the order was posted, in seconds.</summary>
        public double OrderTime { get; }

        /// <summary>The world's time of the shuttle's first contact with anything, in seconds, or infinity when it touched nothing.</summary>
        public double FirstContactTime { get; }

        /// <summary>The records of the ticks after the order, oldest first.</summary>
        public List<Record> Log { get; } = [];

        /// <summary>The lines that the world logged.</summary>
        public List<string> Logged { get; } = [];

        /// <summary>The errors that the world recorded.</summary>
        public IReadOnlyList<string> Errors { get; }

        public void Dispose() => world.Dispose();
    }

    /// <summary>A test component that keeps the world's time of its entity's first collision, or infinity before one.</summary>
    private sealed class FirstContact : IComponent
    {
        public double Time { get; private set; } = double.PositiveInfinity;

        public int Priority => 20;

        public EventMask Wants => EventMask.Of<Collision>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (double.IsPositiveInfinity(Time))
                Time = self.Time;
        }
    }
}
