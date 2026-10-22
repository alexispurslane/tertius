using System.Runtime.CompilerServices;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;

namespace Tertius.Demo;

/// <summary>Tells a shuttle's <see cref="DockingAutopilot"/> to dock in its station.</summary>
public struct Dock : IEventPayload;

/// <summary>
/// A <see cref="DockingAutopilot"/> waits for the hold's tolerances and the roll window, and adds nothing, so the
/// rendezvous's hold flies on.
/// </summary>
public readonly record struct Waiting;

/// <summary>
/// A <see cref="DockingAutopilot"/> spins the shuttle up about the station's axis to the station's rate, and holds it at the
/// hold point.
/// </summary>
/// <param name="StartTime">The world time at which the spin-up started, in seconds.</param>
public readonly record struct SpinUp(double StartTime);

/// <summary>
/// A <see cref="DockingAutopilot"/> flies the shuttle along the station's axis through the slot to the entry's end, with the
/// docked rotation.
/// </summary>
public readonly record struct Entry;

/// <summary>
/// A <see cref="DockingAutopilot"/> flies the shuttle on a straight path in the station's axes from the entry's end to the
/// landing point, in the corner of the deck and the bay's -Z wall, with the docked rotation.
/// </summary>
public readonly record struct MoveOut;

/// <summary>A <see cref="DockingAutopilot"/> has parked the hull, static, at the parked pose, and adds nothing.</summary>
public readonly record struct Docked;

/// <summary>
/// What a <see cref="DockingAutopilot"/> does, in the order of a docking: <see cref="Standby"/>, <see cref="Waiting"/>,
/// <see cref="SpinUp"/>, <see cref="Entry"/>, <see cref="MoveOut"/> and <see cref="Docked"/>, or <see cref="Halted"/>
/// from any of them before <see cref="Docked"/>. It reuses the payload-less cases <see cref="Standby"/> and
/// <see cref="Halted"/> of <see cref="RendezvousState"/>. Each case holds the data that it needs: only the spin-up needs its
/// start time. The default value is <see cref="Standby"/>. This is a custom C# 15 union, like
/// <see cref="RendezvousState"/>.
/// </summary>
[Union]
public readonly struct DockingState : IEquatable<DockingState>
{
    private const byte KindStandby = 0,
        KindWaiting = 1,
        KindSpinUp = 2,
        KindEntry = 3,
        KindMoveOut = 4,
        KindDocked = 5,
        KindHalted = 6;

    // Only the spin-up fills its start time. Every other field is zero, so equality compares every field.
    private readonly double startTime;
    private readonly byte kind;

    public DockingState(Standby standby)
    {
        kind = KindStandby;
    }

    public DockingState(Waiting waiting)
    {
        kind = KindWaiting;
    }

    public DockingState(SpinUp spinUp)
    {
        kind = KindSpinUp;
        startTime = spinUp.StartTime;
    }

    public DockingState(Entry entry)
    {
        kind = KindEntry;
    }

    public DockingState(MoveOut moveOut)
    {
        kind = KindMoveOut;
    }

    public DockingState(Docked docked)
    {
        kind = KindDocked;
    }

    public DockingState(Halted halted)
    {
        kind = KindHalted;
    }

    /// <summary>Always true: every value is a state, and the default value is <see cref="Standby"/>.</summary>
    public bool HasValue => true;

    public object Value => kind switch
    {
        KindWaiting => new Waiting(),
        KindSpinUp => new SpinUp(startTime),
        KindEntry => new Entry(),
        KindMoveOut => new MoveOut(),
        KindDocked => new Docked(),
        KindHalted => new Halted(),
        _ => new Standby()
    };

    public bool TryGetValue(out Standby value)
    {
        value = default;
        return kind == KindStandby;
    }

    public bool TryGetValue(out Waiting value)
    {
        value = default;
        return kind == KindWaiting;
    }

    public bool TryGetValue(out SpinUp value)
    {
        value = new(startTime);
        return kind == KindSpinUp;
    }

    public bool TryGetValue(out Entry value)
    {
        value = default;
        return kind == KindEntry;
    }

    public bool TryGetValue(out MoveOut value)
    {
        value = default;
        return kind == KindMoveOut;
    }

    public bool TryGetValue(out Docked value)
    {
        value = default;
        return kind == KindDocked;
    }

    public bool TryGetValue(out Halted value)
    {
        value = default;
        return kind == KindHalted;
    }

    public bool Equals(DockingState other) => kind == other.kind && startTime.Equals(other.startTime);
    public override bool Equals(object? obj) => obj is DockingState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, startTime);
    public static bool operator ==(DockingState a, DockingState b) => a.Equals(b);
    public static bool operator !=(DockingState a, DockingState b) => !a.Equals(b);

    public override string ToString() => $"DockingState({Value})";
}

/// <summary>
/// A <see cref="DockingAutopilot"/>'s report: the shuttle is within the hold's tolerances and the roll angle lies in its
/// window, at <paramref name="Time"/>, the world time of the tick, in seconds.
/// </summary>
public readonly record struct AtRollWindow(double Time);

/// <summary>A <see cref="DockingAutopilot"/>'s report: the spin-up's time is over, and its tolerances hold.</summary>
public readonly record struct SpunUp;

/// <summary>A <see cref="DockingAutopilot"/>'s report: the shuttle's center is near the entry's end, along the axis and across it.</summary>
public readonly record struct EntryEnded;

/// <summary>
/// A report that a mover has landed. It is a case of two machines. In <see cref="DockingEvent"/>, it is the
/// <see cref="DockingAutopilot"/>'s report that the hull rests at the parked pose within the landing's tolerances. In
/// <see cref="DisembarkEvent"/>, it is the report of <see cref="Disembark"/>'s slide that the person stands on the wheel's
/// floor.
/// </summary>
public readonly record struct Landed;

/// <summary>
/// A <see cref="DockingAutopilot"/>'s report: it cannot fly the docking, for <paramref name="Reason"/>. Its station is gone or
/// owns no frame, its hull has no mass, or the shuttle is in neither the station's frame nor the frame that holds the
/// station.
/// </summary>
public readonly record struct Lost(string Reason);

/// <summary>
/// An event of a <see cref="DockingAutopilot"/>'s machine (see <see cref="DockingAutopilot.Next"/>): the script's order
/// <see cref="Dock"/>, or a report of the case that flew the tick, <see cref="AtRollWindow"/>, <see cref="SpunUp"/>,
/// <see cref="EntryEnded"/>, <see cref="Landed"/> or <see cref="Lost"/>. This is a custom C# 15 union, like
/// <see cref="DockingState"/>. The default value is <see cref="Dock"/>.
/// </summary>
[Union]
public readonly struct DockingEvent : IEquatable<DockingEvent>
{
    private const byte KindDock = 0,
        KindAtRollWindow = 1,
        KindSpunUp = 2,
        KindEntryEnded = 3,
        KindLanded = 4,
        KindLost = 5;

    // Only the roll window fills its time, and only a loss its reason. Every other field is zero or null, so equality
    // compares every field.
    private readonly double time;
    private readonly string? reason;
    private readonly byte kind;

    public DockingEvent(Dock dock)
    {
        kind = KindDock;
    }

    public DockingEvent(AtRollWindow atRollWindow)
    {
        kind = KindAtRollWindow;
        time = atRollWindow.Time;
    }

    public DockingEvent(SpunUp spunUp)
    {
        kind = KindSpunUp;
    }

    public DockingEvent(EntryEnded entryEnded)
    {
        kind = KindEntryEnded;
    }

    public DockingEvent(Landed landed)
    {
        kind = KindLanded;
    }

    public DockingEvent(Lost lost)
    {
        kind = KindLost;
        reason = lost.Reason;
    }

    /// <summary>Always true: every value is an event, and the default value is <see cref="Dock"/>.</summary>
    public bool HasValue => true;

    public object Value => kind switch
    {
        KindAtRollWindow => new AtRollWindow(time),
        KindSpunUp => new SpunUp(),
        KindEntryEnded => new EntryEnded(),
        KindLanded => new Landed(),
        KindLost => new Lost(reason ?? ""),
        _ => new Dock()
    };

    public bool TryGetValue(out Dock value)
    {
        value = default;
        return kind == KindDock;
    }

    public bool TryGetValue(out AtRollWindow value)
    {
        value = new(time);
        return kind == KindAtRollWindow;
    }

    public bool TryGetValue(out SpunUp value)
    {
        value = default;
        return kind == KindSpunUp;
    }

    public bool TryGetValue(out EntryEnded value)
    {
        value = default;
        return kind == KindEntryEnded;
    }

    public bool TryGetValue(out Landed value)
    {
        value = default;
        return kind == KindLanded;
    }

    public bool TryGetValue(out Lost value)
    {
        value = new(reason ?? "");
        return kind == KindLost;
    }

    public bool Equals(DockingEvent other) => kind == other.kind && time.Equals(other.time) && reason == other.reason;
    public override bool Equals(object? obj) => obj is DockingEvent other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, time, reason);
    public static bool operator ==(DockingEvent a, DockingEvent b) => a.Equals(b);
    public static bool operator !=(DockingEvent a, DockingEvent b) => !a.Equals(b);

    public override string ToString() => $"DockingEvent({Value})";
}

/// <summary>
/// The third layer of the shuttle's flight program: it docks the shuttle in its <paramref name="station"/> from the hold
/// point (docs/plans/blue-danube-demo.md, "The docking"). From the spin-up to the landing, it adds an
/// <see cref="AttitudeTarget"/> for the <see cref="AttitudeAutopilot"/> and a <see cref="Translation"/> for the
/// <see cref="ShuttleJetControl"/> in every tick, so the targets and the translations of the layers before it never reach
/// the actuators. It adds no <see cref="Throttle"/>, so the main engine stays off.
/// </summary>
/// <remarks>
/// <para>The docking is one flat state machine. <see cref="DockingState"/> holds every case, and the pure
/// <see cref="Next"/> is the only change of the case. Its events are the script's order <see cref="Dock"/> and the reports
/// of the case that flies the tick. In a tick, the order goes to <see cref="Next"/> first, because it arrived before the
/// tick. Then the case flies the tick and returns at most one report, which goes to <see cref="Next"/> after it, so the new
/// case flies from the next tick on. What each case does:
/// <list type="bullet">
/// <item><see cref="Standby"/>, <see cref="Docked"/> and <see cref="Halted"/>: nothing.</item>
/// <item><see cref="Waiting"/>: nothing, so the rendezvous's hold flies on. It reports <see cref="AtRollWindow"/> when the
/// shuttle is within the hold's tolerances and the roll angle lies in its window (<see cref="SpinUpProfile.InRollWindow"/>
/// ),
/// at most one turn of the station after the order.</item>
/// <item><see cref="SpinUp"/>: the attitude target follows the <see cref="SpinUpProfile"/> for <see cref="SpinUpTime"/>,
/// from still among the stars to rest in the station with the belly toward the deck, and the glide's law holds the hold
/// point. Once the time is over, the target is the docked rotation at rest, and the case reports <see cref="SpunUp"/> when
/// the spin-up's tolerances hold.</item>
/// <item><see cref="Entry"/>: the entry's law flies the shuttle along the axis to <see cref="EntryEnd"/>, with the docked
/// rotation. On the way it enters the station's frame at its bubble. It reports <see cref="EntryEnded"/> within
/// <see cref="EntryEndDistance"/> of the entry's end along the axis and across it.</item>
/// <item><see cref="MoveOut"/>: the move out's law flies the shuttle on a straight path in the station's axes to
/// <see cref="LandingPoint"/>, with the docked rotation. When the hull rests at the parked pose within the landing's
/// tolerances, the case adds nothing and reports <see cref="Landed"/>.</item>
/// </list>
/// Every flying case first reports <see cref="Lost"/> when its station is gone or owns no frame, when its hull has no mass,
/// or when the shuttle is in neither the station's frame nor the frame that holds the station.</para>
/// <para>The side effects come from the old and the new case: from <see cref="MoveOut"/> to <see cref="Docked"/>, the
/// autopilot makes the hull static with <c>Self.Body.SetMotion</c>. Into <see cref="Halted"/>, it logs the reason of the
/// loss once.</para>
/// <para>Every read is in the station's frame, whichever frame holds the shuttle: the shuttle's pose, velocity and angular
/// velocity, and the station's rate relative to the virtual root above the roots, in the station's axes. Outside the
/// bubble the engine converts the reads from the frame that holds the station, with the station's turn, and the autopilot
/// reads the shuttle's and the station's positions there for the difference of the planet's gravity. It assumes what the
/// rendezvous assumes of that frame: it does not turn, its origin is the planet's center, and it has the planet's point
/// gravity. It never names that frame.</para>
/// <para>One law flies every phase in the station's axes: <see cref="ApproachGuidance"/>, with the free acceleration of
/// <see cref="ApproachGuidance.FreeAcceleration"/> at the hull's center of mass, which it reads in each tick from the engine's
/// <see cref="BodyFacts.CenterOfMass"/>. Every write is relative to the shuttle's
/// frame, in its axes, turned with the station frame's pose there. The feed-forward of the attitude target is the target's
/// turn relative to the station plus the station frame's turn relative to the shuttle's frame, so one rule covers both
/// sides of the hand-off.</para>
/// <para>The tick reads the state at the end of the last finished step, and its pushes act in the step of the next tick.
/// Every time constant of the law is 1 s or longer, so the lag costs little.</para>
/// </remarks>
/// <param name="station">The station to dock in. A null or stale handle halts the autopilot at the order.</param>
public sealed class DockingAutopilot(Entity station) : IComponent
{
    /// <summary>How long the spin-up takes, in seconds.</summary>
    public const double SpinUpTime = 30;

    /// <summary>How far beyond the deck and the wall the landing point lies, in meters.</summary>
    public const double LandingPress = 0.1;

    /// <summary>
    /// How near the entry's end the shuttle's center must be for the move out to start, in meters, along the axis and
    /// across it: at y = 3.5 m or less, its tail lies at least 1 m inside the slot wall's inner face.
    /// </summary>
    public const double EntryEndDistance = 0.5;

    /// <summary>How near the docked rotation the hull must be for the spin-up to end, in radians: 1 degree.</summary>
    public const double SpinUpAttitude = Math.PI / 180;

    /// <summary>How slowly the hull must turn relative to the station for the spin-up to end, in rad/s.</summary>
    public const double SpinUpTurn = 0.005;

    /// <summary>How near the parked point the hull must rest across the axis, in x and in z, for the landing to end, in meters.</summary>
    public const double LandedAcross = 0.03;

    /// <summary>How near the parked point the hull must rest along the axis for the landing to end, in meters.</summary>
    public const double LandedAlong = 0.1;

    /// <summary>How near the docked rotation the hull must rest for the landing to end, in radians: 0.5 degrees.</summary>
    public const double LandedAttitude = 0.5 * Math.PI / 180;

    /// <summary>How slowly the hull must move relative to the station for the landing to end, in m/s.</summary>
    public const double LandedSpeed = 0.01;

    /// <summary>How slowly the hull must turn relative to the station for the landing to end, in rad/s.</summary>
    public const double LandedTurn = 0.002;

    /// <summary>The entry's end, in the station's axes: on the axis at the parked pose's y, (0, 3, 0) m.</summary>
    public static Double3 EntryEnd { get; } = new(0, DemoStation.ParkedPose.Position.Y, 0);

    /// <summary>
    /// The landing point, in the station's axes: the parked point moved 0.1 m into the deck and 0.1 m into the bay's -Z wall,
    /// (-6.1, 3, -5.1) m. The move out aims there, so the hull meets the deck and the wall at a set speed and presses on
    /// them lightly.
    /// </summary>
    public static Double3 LandingPoint { get; } =
        DemoStation.ParkedPose.Position + new Double3(-LandingPress, 0, -LandingPress);

    private readonly Entity station = station;

    /// <summary>What the autopilot does now. It starts in <see cref="Standby"/>.</summary>
    public DockingState State { get; private set; } = new Standby();

    // The third layer of the flight program, after the ascent autopilot and the rendezvous autopilot, and before the
    // actuators, which read what the layers add.
    public int Priority => 2;

    public EventMask Wants => EventMask.Of<Dock>().And<Tick>();

    /// <summary>
    /// The docking's one change of case: the case after <paramref name="state"/> when <paramref name="report"/> comes. A
    /// pure function of the two.
    /// <list type="table">
    /// <item><see cref="Standby"/> and the order <see cref="Dock"/>: <see cref="Waiting"/>.</item>
    /// <item><see cref="Waiting"/> and <see cref="AtRollWindow"/>: <see cref="SpinUp"/>, from the report's time.</item>
    /// <item><see cref="SpinUp"/> and <see cref="SpunUp"/>: <see cref="Entry"/>.</item>
    /// <item><see cref="Entry"/> and <see cref="EntryEnded"/>: <see cref="MoveOut"/>.</item>
    /// <item><see cref="MoveOut"/> and <see cref="Landed"/>: <see cref="Docked"/>.</item>
    /// <item><see cref="Waiting"/>, <see cref="SpinUp"/>, <see cref="Entry"/> or <see cref="MoveOut"/>, and <see cref="Lost"/>
    /// :
    /// <see cref="Halted"/>.</item>
    /// <item>Every other pair: <paramref name="state"/>, unchanged. So a <see cref="Dock"/> after the first changes
    /// nothing, and <see cref="Docked"/> and <see cref="Halted"/> are final.</item>
    /// </list>
    /// </summary>
    public static DockingState Next(DockingState state, DockingEvent report) => (state, report) switch
    {
        (Standby, Dock) => new Waiting(),
        (Waiting, AtRollWindow { Time: var time }) => new SpinUp(time),
        (SpinUp, SpunUp) => new Entry(),
        (Entry, EntryEnded) => new MoveOut(),
        (MoveOut, Landed) => new Docked(),
        (Waiting or SpinUp or Entry or MoveOut, Lost) => new Halted(),
        _ => state
    };

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        // The order arrived before the tick, so it goes to the machine first, and the case that it starts flies this tick.
        if (events.Of<Dock>().Any())
            Apply(self, new Dock());
        if (Fly(self, ref events) is { } report)
            Apply(self, report);
    }

    /// <summary>
    /// Changes the case by <see cref="Next"/>, and does the side effects of the change: from <see cref="MoveOut"/> to
    /// <see cref="Docked"/> the hull becomes static, and into <see cref="Halted"/> the reason of the loss is logged once.
    /// </summary>
    private void Apply(Self self, DockingEvent report)
    {
        DockingState old = State;
        State = Next(old, report);
        if (old is MoveOut && State is Docked)
            self.Body.SetMotion(BodyMotion.Static);
        if (old is not Halted && State is Halted && report is Lost { Reason: var reason })
            self.Log($"{nameof(DockingAutopilot)} halts: {reason}.");
    }

    /// <summary>
    /// Flies one tick in the case that the autopilot is in, and returns its report, or null. The cases that do nothing
    /// return null at once. Every other case first returns <see cref="Lost"/> when it cannot fly.
    /// </summary>
    private DockingEvent? Fly(Self self, scoped ref EventList events)
    {
        if (State is not (Waiting or SpinUp or Entry or MoveOut))
            return null;
        FrameTree frames = self.Snapshot.Frames;
        // A destroyed station's frame is gone with it, so the lookup of its frame covers a station that is gone.
        if (!frames.TryGetFrame(station, out FrameHandle stationFrame))
            return new Lost("its station is gone or owns no frame");
        if (!self.Snapshot.TryGetBody(self.Entity, out BodyFacts body) || body.Mass <= 0)
            return new Lost("its hull has no mass");
        bool inside = self.Frame == stationFrame;
        if (!inside && self.Frame != self.Snapshot.FrameOf(station))
            return new Lost("the shuttle is in neither the station's frame nor the frame that holds the station");

        // Every read is in the station's frame, whichever frame holds the shuttle. Outside the bubble the engine converts
        // them from the frame that holds the station, with the station's turn.
        DPose pose = self.PoseIn(stationFrame);
        Double3 velocity = self.VelocityIn(stationFrame), turn = self.AngularVelocityIn(stationFrame);
        // The station's angular velocity relative to the virtual root above the roots, in its own axes.
        Double3 rate = -frames.AngularVelocityRelative(FrameHandle.Null, stationFrame);
        var profile = new SpinUpProfile(rate.Y, SpinUpTime, DemoStation.DockedRotation);
        double time = self.Time;

        switch (State)
        {
            case Waiting:
                return AtTheHold(pose, velocity) &&
                       profile.InRollWindow(profile.RollAngle(pose.Rotation), self.DeltaTime)
                    ? new AtRollWindow(time)
                    : null;
            case SpinUp { StartTime: var start }:
                // The profile ends at the docked rotation at rest, and holds it there until the tolerances hold.
                Steer(self, ref events, frames, stationFrame, inside, body, pose, velocity, rate,
                    profile.Target(time - start), profile.TurnRate(time - start), DemoStation.HoldPoint,
                    ApproachGuidance.ForGlide());
                return time - start >= SpinUpTime && SpinUpDone(pose, turn) ? new SpunUp() : null;
            case Entry:
                Steer(self, ref events, frames, stationFrame, inside, body, pose, velocity, rate,
                    DemoStation.DockedRotation, 0, EntryEnd, ApproachGuidance.ForEntry());
                return pose.Position.Y <= EntryEnd.Y + EntryEndDistance &&
                       FromTheAxis(pose.Position) <= EntryEndDistance
                    ? new EntryEnded()
                    : null;
            default:
                if (RestsAtTheParkedPose(pose, velocity, turn))
                    return new Landed();
                Steer(self, ref events, frames, stationFrame, inside, body, pose, velocity, rate,
                    DemoStation.DockedRotation, 0, LandingPoint, ApproachGuidance.ForMoveOut());
                return null;
        }
    }

    /// <summary>
    /// Adds the attitude target and the translation that fly the hull toward <paramref name="target"/>, turning about the
    /// station's +Y at <paramref name="targetTurn"/> rad/s relative to the station, and its origin toward
    /// <paramref name="point"/> by <paramref name="guidance"/>, all in the station's axes. The writes are relative to the
    /// shuttle's frame, in its axes.
    /// </summary>
    private void Steer(Self self, scoped ref EventList events, FrameTree frames, FrameHandle stationFrame, bool inside,
        in BodyFacts body,
        in DPose pose, Double3 velocity, Double3 rate, DQuaternion target, double targetTurn, Double3 point,
        ApproachGuidance guidance)
    {
        // The station frame's pose in the shuttle's frame turns what the autopilot computed in the station's axes into the
        // axes of the shuttle's frame, in which every write is. Inside the bubble it is the identity.
        DPose stationAxes = frames.Relative(stationFrame, self.Frame);
        Double3 gravityDifference = Double3.Zero;
        if (!inside)
        {
            // Outside the bubble the planet pulls the shuttle and the station's center differently. Inside, a semantic
            // frame gives its contents no tidal gravity.
            double mu = KeplerOrbit.MuFrom(frames.Gravity(self.Frame));
            Double3 shuttle = self.PoseIn(self.Frame).Position;
            gravityDifference = stationAxes.Rotation.Conjugate().Rotate(KeplerOrbit.Acceleration(shuttle, mu) -
                                                                        KeplerOrbit.Acceleration(stationAxes.Position,
                                                                            mu));
        }

        // The spin's terms act at the hull's center of mass, which the engine computes for the hull's shape.
        Double3 free = ApproachGuidance.FreeAcceleration(rate, pose.TransformPoint(body.CenterOfMass), velocity,
            gravityDifference);
        Double3 push = guidance.Push(pose.Position, velocity, point, free);
        Double3 feedForward = stationAxes.Rotation.Rotate(Double3.UnitY * targetTurn) +
                              frames.AngularVelocityRelative(stationFrame, self.Frame);
        events.Add(new AttitudeTarget
        {
            Rotation = stationAxes.Rotation * target, FeedForward = feedForward.ToVector3(),
            MaxTurnRate = AttitudeAutopilot.MaxTurnRate
        });
        events.Add(new Translation { Force = stationAxes.Rotation.Rotate(push) * body.Mass });
    }

    /// <summary>
    /// True when the shuttle is within the hold's tolerances, in the station's axes: within
    /// <see cref="RendezvousAutopilot.HoldDistanceTolerance"/> of the hold point, slower than
    /// <see cref="RendezvousAutopilot.HoldSpeedTolerance"/>, and its nose within
    /// <see cref="RendezvousAutopilot.HoldNoseTolerance"/>
    /// of minus the axis.
    /// </summary>
    private static bool AtTheHold(in DPose pose, Double3 velocity) =>
        (pose.Position - DemoStation.HoldPoint).Length < RendezvousAutopilot.HoldDistanceTolerance
        && velocity.Length < RendezvousAutopilot.HoldSpeedTolerance
        && AngleBetween(pose.Rotation.Rotate(-Double3.UnitZ), -Double3.UnitY) < RendezvousAutopilot.HoldNoseTolerance;

    /// <summary>
    /// True when the spin-up's tolerances hold: within <see cref="SpinUpAttitude"/> of the docked rotation, turning slower
    /// than <see cref="SpinUpTurn"/> relative to the station, and within
    /// <see cref="RendezvousAutopilot.HoldDistanceTolerance"/>
    /// of the hold point.
    /// </summary>
    private static bool SpinUpDone(in DPose pose, Double3 turn) =>
        pose.Rotation.AngularVelocityTo(DemoStation.DockedRotation, 1).Length < SpinUpAttitude
        && turn.Length < SpinUpTurn
        && (pose.Position - DemoStation.HoldPoint).Length < RendezvousAutopilot.HoldDistanceTolerance;

    /// <summary>
    /// True when the hull rests at the parked pose within the landing's tolerances: x and z within <see cref="LandedAcross"/>
    /// of the parked point, y within <see cref="LandedAlong"/>, the rotation within <see cref="LandedAttitude"/> of the
    /// docked rotation, slower than <see cref="LandedSpeed"/> and turning slower than <see cref="LandedTurn"/>.
    /// </summary>
    private static bool RestsAtTheParkedPose(in DPose pose, Double3 velocity, Double3 turn)
    {
        Double3 off = pose.Position - DemoStation.ParkedPose.Position;
        return Math.Abs(off.X) < LandedAcross && Math.Abs(off.Z) < LandedAcross && Math.Abs(off.Y) < LandedAlong
               && pose.Rotation.AngularVelocityTo(DemoStation.DockedRotation, 1).Length < LandedAttitude
               && velocity.Length < LandedSpeed && turn.Length < LandedTurn;
    }

    /// <summary>How far <paramref name="position"/> lies from the station's axis, +Y, in meters.</summary>
    private static double FromTheAxis(Double3 position) => Math.Sqrt(position.X * position.X + position.Z * position.Z);

    /// <summary>The angle between two directions, in radians.</summary>
    private static double AngleBetween(Double3 a, Double3 b) =>
        Math.Acos(Math.Clamp(Double3.Dot(a.Normalized(), b.Normalized()), -1, 1));
}
