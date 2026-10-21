using System.Runtime.CompilerServices;
using System.Numerics;
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
/// start time. The default value is <see cref="Standby"/>. This is a custom C# 15 union, like <see cref="RendezvousState"/>.
/// </summary>
[Union]
public readonly struct DockingState : IEquatable<DockingState>
{
    private const byte KindStandby = 0, KindWaiting = 1, KindSpinUp = 2, KindEntry = 3, KindMoveOut = 4, KindDocked = 5, KindHalted = 6;

    // Only the spin-up fills its start time. Every other field is zero, so equality compares every field.
    private readonly double startTime;
    private readonly byte kind;

    public DockingState(Standby standby) { kind = KindStandby; }
    public DockingState(Waiting waiting) { kind = KindWaiting; }
    public DockingState(SpinUp spinUp) { kind = KindSpinUp; startTime = spinUp.StartTime; }
    public DockingState(Entry entry) { kind = KindEntry; }
    public DockingState(MoveOut moveOut) { kind = KindMoveOut; }
    public DockingState(Docked docked) { kind = KindDocked; }
    public DockingState(Halted halted) { kind = KindHalted; }

    /// <summary>Always true: every value is a state, and the default value is <see cref="Standby"/>.</summary>
    public bool HasValue => true;

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
        value = new SpinUp(startTime);
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

    public object Value => kind switch
    {
        KindWaiting => new Waiting(),
        KindSpinUp => new SpinUp(startTime),
        KindEntry => new Entry(),
        KindMoveOut => new MoveOut(),
        KindDocked => new Docked(),
        KindHalted => new Halted(),
        _ => new Standby(),
    };

    public bool Equals(DockingState other) => kind == other.kind && startTime.Equals(other.startTime);
    public override bool Equals(object? obj) => obj is DockingState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, startTime);
    public static bool operator ==(DockingState a, DockingState b) => a.Equals(b);
    public static bool operator !=(DockingState a, DockingState b) => !a.Equals(b);

    public override string ToString() => $"DockingState({Value})";
}

/// <summary>
/// The third layer of the shuttle's flight program: it docks the shuttle in its <paramref name="station"/> from the hold
/// point (docs/plans/blue-danube-demo.md, "The docking"). From the spin-up to the landing, it adds an
/// <see cref="AttitudeTarget"/> for the <see cref="AttitudeAutopilot"/> and a <see cref="Translation"/> for the
/// <see cref="TranslationThrusters"/> in every tick, so the targets and the translations of the layers before it never reach
/// the actuators. It adds no <see cref="Throttle"/>, so the main engine stays off. The docking goes through the cases of
/// <see cref="DockingState"/>:
/// <list type="bullet">
/// <item><see cref="Standby"/>: it adds nothing. A <see cref="Dock"/> order starts the wait in the same tick.</item>
/// <item><see cref="Waiting"/>: it adds nothing, so the rendezvous's hold flies on. When the shuttle is within the hold's
/// tolerances and the roll angle lies in its window (<see cref="SpinUpProfile.InRollWindow"/>), the spin-up starts, at most
/// one turn of the station after the order.</item>
/// <item><see cref="SpinUp"/>: the attitude target follows the <see cref="SpinUpProfile"/> for <see cref="SpinUpTime"/>,
/// from still among the stars to rest in the station with the belly toward the deck, and the glide's law holds the hold
/// point. Once the time is over and the spin-up's tolerances hold, the entry starts. Until they hold, the target is the
/// docked rotation at rest.</item>
/// <item><see cref="Entry"/>: the entry's law flies the shuttle along the axis to <see cref="EntryEnd"/>, with the docked
/// rotation. On the way it enters the station's frame at its bubble. Within <see cref="EntryEndDistance"/> of the entry's
/// end along the axis and across it, the move out starts.</item>
/// <item><see cref="MoveOut"/>: the move out's law flies the shuttle on a straight path in the station's axes to
/// <see cref="LandingPoint"/>, with the docked rotation. When the hull rests at the parked pose within the landing's
/// tolerances, the autopilot makes the hull static with <c>Self.Body.SetMotion</c>, and docks in the same tick.</item>
/// <item><see cref="Docked"/>: it adds nothing.</item>
/// </list>
/// The autopilot halts, logs the reason once and adds nothing more (<see cref="Halted"/>) when its station is gone or owns
/// no frame, when its hull has no mass, or when the shuttle is in neither the station's frame nor the frame that holds the
/// station.
/// </summary>
/// <remarks>
/// <para>Every read is in the station's frame, whichever frame holds the shuttle: the shuttle's pose, velocity and angular
/// velocity, and the station's rate relative to the virtual root above the roots, in the station's axes. Outside the
/// bubble the engine converts the reads from the frame that holds the station, with the station's turn, and the autopilot
/// reads the shuttle's and the station's positions there for the difference of the planet's gravity. It assumes what the
/// rendezvous assumes of that frame: it does not turn, its origin is the planet's center, and it has the planet's point
/// gravity. It never names that frame.</para>
/// <para>One law flies every phase in the station's axes: <see cref="ApproachGuidance"/>, with the free acceleration of
/// <see cref="ApproachGuidance.FreeAcceleration"/> at the hull's center of mass. Every write is relative to the shuttle's
/// frame, in its axes, turned with the station frame's pose there. The feed-forward of the attitude target is the target's
/// turn relative to the station plus the station frame's turn relative to the shuttle's frame, so one rule covers both
/// sides of the hand-off.</para>
/// <para>The tick reads the state at the end of the last finished step, and its pushes act in the step of the next tick.
/// Every time constant of the law is 1 s or longer, so the lag costs little.</para>
/// </remarks>
/// <param name="station">The station to dock in. A null or stale handle halts the autopilot at the order.</param>
/// <param name="centerOfMass">The hull's center of mass in its own axes, in meters, where the spin's terms act.</param>
public sealed class DockingAutopilot(Entity station, Vector3 centerOfMass) : IComponent
{
    /// <summary>How long the spin-up takes, in seconds.</summary>
    public const double SpinUpTime = 30;

    /// <summary>The entry's end, in the station's axes: on the axis at the parked pose's y, (0, 3, 0) m.</summary>
    public static Double3 EntryEnd { get; } = new(0, DemoStation.ParkedPose.Position.Y, 0);

    /// <summary>
    /// The landing point, in the station's axes: the parked point moved 0.1 m into the deck and 0.1 m into the bay's -Z wall,
    /// (-6.1, 3, -5.1) m. The move out aims there, so the hull meets the deck and the wall at a set speed and presses on
    /// them lightly.
    /// </summary>
    public static Double3 LandingPoint { get; } = DemoStation.ParkedPose.Position + new Double3(-LandingPress, 0, -LandingPress);

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

    private readonly Entity station = station;
    private readonly Double3 centerOfMass = (Double3)centerOfMass;

    /// <summary>What the autopilot does now. It starts in <see cref="Standby"/>.</summary>
    public DockingState State { get; private set; } = new Standby();

    // The third layer of the flight program, after the ascent autopilot and the rendezvous autopilot, and before the
    // actuators, which read what the layers add.
    public int Priority => 2;

    public EventMask Wants => EventMask.Of<Dock>().And<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (State is Halted or Docked)
            return;
        if (State is Standby)
        {
            if (!events.Of<Dock>().Any())
                return;
            State = new Waiting();
        }
        FrameTree frames = self.Snapshot.Frames;
        // A destroyed station's frame is gone with it, so the lookup of its frame covers a station that is gone.
        if (!frames.TryGetFrame(station, out FrameHandle stationFrame))
        {
            Halt(self, "its station is gone or owns no frame");
            return;
        }
        if (!self.Snapshot.TryGetBody(self.Entity, out BodyFacts body) || body.Mass <= 0)
        {
            Halt(self, "its hull has no mass");
            return;
        }
        bool inside = self.Frame == stationFrame;
        if (!inside && self.Frame != self.Snapshot.FrameOf(station))
        {
            Halt(self, "the shuttle is in neither the station's frame nor the frame that holds the station");
            return;
        }

        // Every read is in the station's frame, whichever frame holds the shuttle. Outside the bubble the engine converts
        // them from the frame that holds the station, with the station's turn.
        DPose pose = self.PoseIn(stationFrame);
        Double3 velocity = self.VelocityIn(stationFrame), turn = (Double3)self.AngularVelocityIn(stationFrame);
        // The station's angular velocity relative to the virtual root above the roots, in its own axes.
        Double3 rate = -frames.AngularVelocityRelative(FrameHandle.Null, stationFrame);
        var profile = new SpinUpProfile(rate.Y, SpinUpTime, DemoStation.DockedRotation);
        double time = self.Time;

        if (State is Waiting && AtTheHold(pose, velocity) && profile.InRollWindow(profile.RollAngle(pose.Rotation), self.DeltaTime))
            State = new SpinUp(time);
        if (State is SpinUp spinUp && time - spinUp.StartTime >= SpinUpTime && SpinUpDone(pose, turn))
            State = new Entry();
        if (State is Entry && pose.Position.Y <= EntryEnd.Y + EntryEndDistance && FromTheAxis(pose.Position) <= EntryEndDistance)
            State = new MoveOut();
        if (State is MoveOut && Landed(pose, velocity, turn))
        {
            self.Body.SetMotion(BodyMotion.Static);
            State = new Docked();
            return;
        }

        (DQuaternion target, double targetTurn, Double3 point, ApproachGuidance guidance) = State.Value switch
        {
            SpinUp { StartTime: var start } => SpinUpDone(pose, turn) && time - start >= SpinUpTime
                ? (DemoStation.DockedRotation, 0.0, DemoStation.HoldPoint, ApproachGuidance.ForGlide())
                : (profile.Target(time - start), profile.TurnRate(time - start), DemoStation.HoldPoint, ApproachGuidance.ForGlide()),
            Entry => (DemoStation.DockedRotation, 0.0, EntryEnd, ApproachGuidance.ForEntry()),
            MoveOut => (DemoStation.DockedRotation, 0.0, LandingPoint, ApproachGuidance.ForMoveOut()),
            _ => (DQuaternion.Identity, 0.0, Double3.Zero, default(ApproachGuidance)),
        };
        if (State.Value is not (SpinUp or Entry or MoveOut))
            return;

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
            gravityDifference = stationAxes.Rotation.Conjugate().Rotate(KeplerOrbit.Acceleration(shuttle, mu) - KeplerOrbit.Acceleration(stationAxes.Position, mu));
        }
        Double3 free = ApproachGuidance.FreeAcceleration(rate, pose.TransformPoint(centerOfMass), velocity, gravityDifference);
        Double3 push = guidance.Push(pose.Position, velocity, point, free);
        Double3 feedForward = stationAxes.Rotation.Rotate(Double3.UnitY * targetTurn) + frames.AngularVelocityRelative(stationFrame, self.Frame);
        events.Add(new AttitudeTarget { Rotation = stationAxes.Rotation * target, FeedForward = feedForward.ToVector3(), MaxTurnRate = AttitudeAutopilot.MaxTurnRate });
        events.Add(new Translation { Force = stationAxes.Rotation.Rotate(push) * body.Mass });
    }

    /// <summary>
    /// True when the shuttle is within the hold's tolerances, in the station's axes: within
    /// <see cref="RendezvousAutopilot.HoldDistanceTolerance"/> of the hold point, slower than
    /// <see cref="RendezvousAutopilot.HoldSpeedTolerance"/>, and its nose within <see cref="RendezvousAutopilot.HoldNoseTolerance"/>
    /// of minus the axis.
    /// </summary>
    private static bool AtTheHold(in DPose pose, Double3 velocity) =>
        (pose.Position - DemoStation.HoldPoint).Length < RendezvousAutopilot.HoldDistanceTolerance
        && velocity.Length < RendezvousAutopilot.HoldSpeedTolerance
        && AngleBetween(pose.Rotation.Rotate(-Double3.UnitZ), -Double3.UnitY) < RendezvousAutopilot.HoldNoseTolerance;

    /// <summary>
    /// True when the spin-up's tolerances hold: within <see cref="SpinUpAttitude"/> of the docked rotation, turning slower
    /// than <see cref="SpinUpTurn"/> relative to the station, and within <see cref="RendezvousAutopilot.HoldDistanceTolerance"/>
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
    private static bool Landed(in DPose pose, Double3 velocity, Double3 turn)
    {
        Double3 off = pose.Position - DemoStation.ParkedPose.Position;
        return Math.Abs(off.X) < LandedAcross && Math.Abs(off.Z) < LandedAcross && Math.Abs(off.Y) < LandedAlong
            && pose.Rotation.AngularVelocityTo(DemoStation.DockedRotation, 1).Length < LandedAttitude
            && velocity.Length < LandedSpeed && turn.Length < LandedTurn;
    }

    /// <summary>How far <paramref name="position"/> lies from the station's axis, +Y, in meters.</summary>
    private static double FromTheAxis(Double3 position) => Math.Sqrt(position.X * position.X + position.Z * position.Z);

    /// <summary>The angle between two directions, in radians.</summary>
    private static double AngleBetween(Double3 a, Double3 b) => Math.Acos(Math.Clamp(Double3.Dot(a.Normalized(), b.Normalized()), -1, 1));

    private void Halt(Self self, string reason)
    {
        State = new Halted();
        self.Log($"{nameof(DockingAutopilot)} halts: {reason}.");
    }
}
