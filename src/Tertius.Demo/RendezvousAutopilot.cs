using System.Runtime.CompilerServices;
using Tertius.Frames.Rails;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>Tells a shuttle's <see cref="RendezvousAutopilot"/> to fly to its station.</summary>
public struct Rendezvous : IEventPayload;

/// <summary>A <see cref="RendezvousAutopilot"/> waits for a <see cref="Rendezvous"/> order, and adds nothing.</summary>
public readonly record struct Standby;

/// <summary>
/// A <see cref="RendezvousAutopilot"/> waits in the parking orbit for its window, and adds nothing, so the orbit attitude of
/// the ascent autopilot flies on.
/// </summary>
/// <param name="Guidance">The guidance of the transfer, made at the order.</param>
public readonly record struct Phasing(TransferGuidance Guidance);

/// <summary>
/// A <see cref="RendezvousAutopilot"/> burns and trims onto the two-body path that reaches the arrival point at the arrival
/// time, and coasts at its end (see <see cref="TransferGuidance.Coasts"/>).
/// </summary>
/// <param name="Guidance">The guidance of the transfer, made at the order.</param>
/// <param name="ArrivalTime">The time at which the shuttle reaches the arrival point, in the world's seconds.</param>
public readonly record struct Transfer(TransferGuidance Guidance, double ArrivalTime);

/// <summary>A <see cref="RendezvousAutopilot"/> stops the shuttle at the arrival point with the main engine.</summary>
/// <param name="Guidance">The approach guidance of the brake (see <see cref="ApproachGuidance.ForBrake"/>).</param>
public readonly record struct Brake(ApproachGuidance Guidance);

/// <summary>
/// A <see cref="RendezvousAutopilot"/> turns the shuttle's nose to the slot and flies the shuttle along the station's axis to
/// the hold point, on the translation thrusters.
/// </summary>
/// <param name="Guidance">The approach guidance of the glide (see <see cref="ApproachGuidance.ForGlide"/>).</param>
public readonly record struct Glide(ApproachGuidance Guidance);

/// <summary>
/// A <see cref="RendezvousAutopilot"/> holds the shuttle at the hold point with its nose at the slot, until a later layer of
/// the flight program speaks.
/// </summary>
/// <param name="Guidance">The approach guidance of the glide (see <see cref="ApproachGuidance.ForGlide"/>).</param>
public readonly record struct Hold(ApproachGuidance Guidance);

/// <summary>
/// A <see cref="RendezvousAutopilot"/> cannot fly: its station is gone, its frame has no point gravity, its hull has no
/// mass, or the shuttle has left the frame that holds the station. It adds nothing more.
/// </summary>
public readonly record struct Halted;

/// <summary>
/// What a <see cref="RendezvousAutopilot"/> does, in the order of a flight: <see cref="Standby"/>, <see cref="Phasing"/>,
/// <see cref="Transfer"/>, <see cref="Brake"/>, <see cref="Glide"/> and <see cref="Hold"/>, or <see cref="Halted"/> from
/// any of them. Each case holds the data that it needs, so no state reads a value that only another state sets. The
/// default value is <see cref="Standby"/>. This is a custom C# 15 union, like <see cref="AscentState"/>.
/// </summary>
[Union]
public readonly struct RendezvousState : IEquatable<RendezvousState>
{
    private const byte KindStandby = 0, KindPhasing = 1, KindTransfer = 2, KindBrake = 3, KindGlide = 4, KindHold = 5, KindHalted = 6;

    // The phasing and the transfer fill the transfer guidance, and only the transfer the arrival time. The brake, the glide
    // and the hold fill the approach guidance. Every other field is zero, so equality compares every field.
    private readonly TransferGuidance transfer;
    private readonly double arrivalTime;
    private readonly ApproachGuidance approach;
    private readonly byte kind;

    public RendezvousState(Standby standby) { kind = KindStandby; }
    public RendezvousState(Phasing phasing) { kind = KindPhasing; transfer = phasing.Guidance; }
    public RendezvousState(Transfer transfer) { kind = KindTransfer; this.transfer = transfer.Guidance; arrivalTime = transfer.ArrivalTime; }
    public RendezvousState(Brake brake) { kind = KindBrake; approach = brake.Guidance; }
    public RendezvousState(Glide glide) { kind = KindGlide; approach = glide.Guidance; }
    public RendezvousState(Hold hold) { kind = KindHold; approach = hold.Guidance; }
    public RendezvousState(Halted halted) { kind = KindHalted; }

    /// <summary>Always true: every value is a state, and the default value is <see cref="Standby"/>.</summary>
    public bool HasValue => true;

    public bool TryGetValue(out Standby value)
    {
        value = default;
        return kind == KindStandby;
    }

    public bool TryGetValue(out Phasing value)
    {
        value = new Phasing(transfer);
        return kind == KindPhasing;
    }

    public bool TryGetValue(out Transfer value)
    {
        value = new Transfer(transfer, arrivalTime);
        return kind == KindTransfer;
    }

    public bool TryGetValue(out Brake value)
    {
        value = new Brake(approach);
        return kind == KindBrake;
    }

    public bool TryGetValue(out Glide value)
    {
        value = new Glide(approach);
        return kind == KindGlide;
    }

    public bool TryGetValue(out Hold value)
    {
        value = new Hold(approach);
        return kind == KindHold;
    }

    public bool TryGetValue(out Halted value)
    {
        value = default;
        return kind == KindHalted;
    }

    public object Value => kind switch
    {
        KindPhasing => new Phasing(transfer),
        KindTransfer => new Transfer(transfer, arrivalTime),
        KindBrake => new Brake(approach),
        KindGlide => new Glide(approach),
        KindHold => new Hold(approach),
        KindHalted => new Halted(),
        _ => new Standby(),
    };

    public bool Equals(RendezvousState other) =>
        kind == other.kind && transfer == other.transfer && arrivalTime.Equals(other.arrivalTime) && approach == other.approach;
    public override bool Equals(object? obj) => obj is RendezvousState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, transfer, arrivalTime, approach);
    public static bool operator ==(RendezvousState a, RendezvousState b) => a.Equals(b);
    public static bool operator !=(RendezvousState a, RendezvousState b) => !a.Equals(b);

    public override string ToString() => $"RendezvousState({Value})";
}

/// <summary>
/// The places, the time and the window of a rendezvous (docs/plans/blue-danube-demo.md, "The orbits, the window and the
/// times"). The demo flies <see cref="Demo"/>. A test flies a short plan, because the demo's takes an hour.
/// </summary>
/// <param name="TransferTime">How long the transfer takes, from the window to the arrival time, in seconds.</param>
/// <param name="ArrivalDistance">How far the arrival point lies along the station's axis from its center, in meters.</param>
/// <param name="HoldDistance">How far the hold point lies along the station's axis from its center, in meters.</param>
/// <param name="WindowOpens">
/// The transfer angle at which the window opens, in radians. The angle falls while the shuttle waits, because the lower
/// shuttle gains on the station, so the window is open while the angle lies from <paramref name="WindowCloses"/> to this.
/// </param>
/// <param name="WindowCloses">
/// The transfer angle at which the window closes, in radians. The window lies inside half a turn, more than
/// <see cref="KeplerOrbit.LambertRefusedAngle"/> from 0 and from 180 degrees, where Lambert's problem has no plane.
/// </param>
/// <param name="WeightlessTurnRate">
/// The fastest turn of the nose in the trim and the coast of the transfer, while the cabin is weightless, in radians per
/// second, at most <see cref="AttitudeAutopilot.MaxTurnRate"/>. The turn from the burn's nose to the brake's direction
/// must end within the trim and the coast.
/// </param>
public readonly record struct RendezvousPlan(double TransferTime, double ArrivalDistance, double HoldDistance, double WindowOpens, double WindowCloses,
    double WeightlessTurnRate)
{
    /// <summary>
    /// The demo's plan: a transfer of 170 degrees in 2,560 s, the time of the cheapest transfer of 170 degrees between the
    /// parking orbit and the station's, to the arrival point 1,500 m out on the axis, and then to
    /// <see cref="DemoScenes.DemoStation.HoldPoint"/>, 150 m out. The window is open from 170 down to 150 degrees.
    /// </summary>
    /// <remarks>
    /// The weightless turn rate is 0.002 rad/s, about 0.11 degrees per second. A turn of the hull gives the strapped
    /// person a felt gravity: the centrifugal term ω² r, and, while the <see cref="AttitudeAutopilot"/> brings the hull to
    /// its rate in <see cref="AttitudeAutopilot.RateTime"/>, the Euler term ω r / RateTime. At the seat, 4 m from the hull's
    /// center, the Euler term of this rate is 0.024 m/s², less than half <see cref="FrameTree.ZeroGravityThreshold"/>, so
    /// the cabin stays weightless and the person keeps its up. At <see cref="AttitudeAutopilot.MaxTurnRate"/>, the turn of
    /// about 160 degrees from the burn's nose to the brake's direction gave about 0.1 m/s² there and turned the person onto
    /// its side. This rate turns it in about 1,400 s, inside the trim of about 2,450 s.
    /// </remarks>
    public static RendezvousPlan Demo { get; } = new(TransferTime: 2560, ArrivalDistance: 1500, HoldDistance: DemoScenes.DemoStation.HoldPoint.Y,
        WindowOpens: 170 * Math.PI / 180, WindowCloses: 150 * Math.PI / 180, WeightlessTurnRate: 0.002);
}

/// <summary>
/// The second layer of the shuttle's flight program: it flies the shuttle from its parking orbit to the hold point of its
/// <paramref name="station"/>, on the <paramref name="plan"/> (docs/plans/blue-danube-demo.md, "The rendezvous autopilot").
/// In the ticks that it steers, it adds an <see cref="AttitudeTarget"/> for the <see cref="AttitudeAutopilot"/>, and a
/// <see cref="Throttle"/> for the <see cref="MainEngine"/> and a <see cref="Translation"/> for the
/// <see cref="TranslationThrusters"/> when they push. Those actuators read them after it (see <see cref="Priority"/>). Its
/// target overrides the orbit attitude of the <see cref="AscentAutopilot"/>, which runs before it. It plans with
/// <paramref name="maxThrust"/>, the main engine's thrust at full throttle. The flight goes through the cases of
/// <see cref="RendezvousState"/>:
/// <list type="bullet">
/// <item><see cref="Standby"/>: it adds nothing. A <see cref="Rendezvous"/> order makes the <see cref="TransferGuidance"/>
/// from the point gravity of the shuttle's frame, and starts the phasing in the same tick.</item>
/// <item><see cref="Phasing"/>: it adds nothing. When the transfer angle to the arrival point of one transfer time later
/// lies from the plan's <see cref="RendezvousPlan.WindowCloses"/> to its <see cref="RendezvousPlan.WindowOpens"/>, the
/// transfer starts, with the arrival time one transfer time from now.</item>
/// <item><see cref="Transfer"/>: while the velocity to gain is longer than <see cref="TransferGuidance.SpeedTolerance"/>,
/// the nose turns toward it, the main engine gives the part of the push along the nose, and the translation thrusters the
/// rest. Below that, the thrusters alone trim it, and the nose turns toward the brake's direction. In the coast, in the
/// last <see cref="TransferGuidance.CoastTime"/> or within <see cref="TransferGuidance.CoastAngle"/> of the arrival point,
/// whichever comes first, the autopilot solves nothing more, and the nose turns against the velocity relative to the
/// station. In the trim and the coast, the cabin is weightless, so the nose turns at most at the plan's
/// <see cref="RendezvousPlan.WeightlessTurnRate"/>. The brake starts in the coast, where the braking curve of the brake reaches the shuttle, or at
/// the arrival time.</item>
/// <item><see cref="Brake"/>: the brake's <see cref="ApproachGuidance"/> flies the shuttle to the arrival point, with the
/// nose along its push, and the main engine and the thrusters split the push as in the burn. Within
/// <see cref="BrakeEndDistance"/> of the arrival point, and slower than <see cref="BrakeEndSpeed"/> relative to the
/// station, the glide starts.</item>
/// <item><see cref="Glide"/>: the glide's guidance flies the shuttle along the axis to the hold point on the thrusters
/// alone, with the nose at the slot, along minus the axis. Within the hold's tolerances, the hold starts.</item>
/// <item><see cref="Hold"/>: the same law holds the shuttle at the hold point, until a later layer speaks.</item>
/// </list>
/// In every case that steers, the roof turns away from the planet across the nose. The autopilot halts, logs the reason
/// once and adds nothing more (<see cref="Halted"/>) when the station is gone, when the shuttle's frame has no point
/// gravity at the order, when its hull has no mass, or when the shuttle is not in the frame that holds the station.
/// </summary>
/// <remarks>
/// <para>Every read is in the shuttle's frame, <c>self.Frame</c>, which must not turn and must have its origin at the
/// planet's center, as <c>earth</c> does, which holds the station and the shuttle after the hand-off of the climb: the shuttle's pose and velocity, and the station's pose and velocity
/// through the snapshot. The station's axis is its rotation applied to +Y. Every write is relative to the same frame.
/// Both the shuttle and the station own frames, so both velocities are the instant velocities of their frames' last step
/// records, and their difference has no bias.</para>
/// <para>The tick reads the state at the end of the last finished step, and its pushes act in the step of the next tick.
/// Every time constant of the guidance is 0.5 s or longer, so the lag costs little, and the guidance corrects every error
/// from the state alone.</para>
/// </remarks>
/// <param name="station">The station to fly to. A null or stale handle halts the autopilot at the order.</param>
/// <param name="plan">The places, the time and the window of the rendezvous.</param>
/// <param name="maxThrust">The thrust of the hull's main engine at full throttle, in newtons.</param>
public sealed class RendezvousAutopilot(Entity station, RendezvousPlan plan, double maxThrust) : IComponent
{
    /// <summary>How near the arrival point the shuttle must be for the brake to end, in meters.</summary>
    public const double BrakeEndDistance = 10;

    /// <summary>How slow relative to the station the shuttle must be for the brake to end, in m/s.</summary>
    public const double BrakeEndSpeed = 0.1;

    /// <summary>How near the hold point the shuttle must be for the hold to start, in meters.</summary>
    public const double HoldDistanceTolerance = 0.5;

    /// <summary>How slow relative to the station the shuttle must be for the hold to start, in m/s.</summary>
    public const double HoldSpeedTolerance = 0.02;

    /// <summary>How near minus the station's axis the nose must point for the hold to start, in radians: 1 degree.</summary>
    public const double HoldNoseTolerance = Math.PI / 180;

    private readonly Entity station = station;

    private readonly RendezvousPlan plan =
        plan.TransferTime > TransferGuidance.CoastTime && double.IsFinite(plan.TransferTime)
        && plan.ArrivalDistance > 0 && double.IsFinite(plan.ArrivalDistance) && plan.HoldDistance > 0 && double.IsFinite(plan.HoldDistance)
        && plan.WindowCloses > KeplerOrbit.LambertRefusedAngle && plan.WindowCloses < plan.WindowOpens
        && plan.WindowOpens < Math.PI - KeplerOrbit.LambertRefusedAngle
        && plan.WeightlessTurnRate > 0 && plan.WeightlessTurnRate <= AttitudeAutopilot.MaxTurnRate
            ? plan
            : throw new ArgumentException($"A plan needs a transfer longer than the coast, positive distances, a window inside half a turn and more than a degree from 0 and from 180 degrees, and a weightless turn rate above 0 and at most the attitude autopilot's, not {plan}.", nameof(plan));

    private readonly double maxThrust = maxThrust > 0 && double.IsFinite(maxThrust)
        ? maxThrust
        : throw new ArgumentOutOfRangeException(nameof(maxThrust), maxThrust, "The thrust of the main engine must be positive and finite.");

    /// <summary>What the autopilot does now. It starts in <see cref="Standby"/>.</summary>
    public RendezvousState State { get; private set; } = new Standby();

    // The second layer of the flight program, after the ascent autopilot and before the docking autopilot of step 5, and
    // before the actuators, which read what the layers add.
    public int Priority => 1;

    public EventMask Wants => EventMask.Of<Rendezvous>().And<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (State is Halted)
            return;
        if (State is Standby)
        {
            if (!events.Of<Rendezvous>().Any())
                return;
            double mu = KeplerOrbit.MuFrom(self.Snapshot.Frames.Gravity(self.Frame));
            if (mu <= 0)
            {
                Halt(self, "its frame has no point gravity to fly in");
                return;
            }
            State = new Phasing(new TransferGuidance(mu));
        }
        if (!self.Snapshot.IsAlive(station))
        {
            Halt(self, "its station is gone");
            return;
        }
        if (self.Snapshot.FrameOf(station) != self.Frame)
        {
            Halt(self, "the shuttle is not in the frame that holds its station");
            return;
        }
        if (!self.Snapshot.TryGetBody(self.Entity, out BodyFacts body) || body.Mass <= 0)
        {
            Halt(self, "its hull has no mass");
            return;
        }

        DPose pose = self.PoseIn(self.Frame);
        Double3 position = pose.Position, velocity = self.VelocityIn(self.Frame), nose = pose.Rotation.Rotate(-Double3.UnitZ);
        self.Snapshot.TryGetPoseIn(station, self.Frame, out DPose stationPose);
        self.Snapshot.TryGetVelocityIn(station, self.Frame, out Double3 stationVelocity);
        Double3 center = stationPose.Position, axis = stationPose.Rotation.Rotate(Double3.UnitY);
        Double3 relative = velocity - stationVelocity;
        Double3 arrivalPoint = center + axis * plan.ArrivalDistance, holdPoint = center + axis * plan.HoldDistance;
        double time = self.Time, mass = body.Mass;

        if (State is Phasing phasing && InWindow(phasing.Guidance, position, center, stationVelocity, axis))
            State = new Transfer(phasing.Guidance, time + plan.TransferTime);
        bool coasts = State is Transfer current
            && current.Guidance.Coasts(position, center, stationVelocity, axis, plan.ArrivalDistance, current.ArrivalTime - time);
        if (State is Transfer transfer && BrakeStarts(transfer, time, coasts, (position - arrivalPoint).Length, relative.Length))
            State = new Brake(ApproachGuidance.ForBrake(transfer.Guidance.Mu));
        if (State is Brake brake && (position - arrivalPoint).Length < BrakeEndDistance && relative.Length < BrakeEndSpeed)
            State = new Glide(ApproachGuidance.ForGlide(brake.Guidance.Mu));
        if (State is Glide glide && (position - holdPoint).Length < HoldDistanceTolerance && relative.Length < HoldSpeedTolerance
            && Math.Acos(Math.Clamp(Double3.Dot(nose, -axis), -1, 1)) < HoldNoseTolerance)
            State = new Hold(glide.Guidance);

        switch (State)
        {
            case Transfer { Guidance: var guidance, ArrivalTime: var arrivalTime }:
                if (coasts)
                {
                    Face(ref events, -relative, pose, plan.WeightlessTurnRate);
                    break;
                }
                TransferSteering steering = guidance.Steer(position, velocity, center, stationVelocity, axis, plan.ArrivalDistance, arrivalTime - time);
                if (steering.Burns)
                {
                    Face(ref events, steering.ToGain, pose, AttitudeAutopilot.MaxTurnRate);
                    Burn(ref events, steering.Push, nose, mass);
                }
                else
                {
                    Face(ref events, steering.BrakeDirection, pose, plan.WeightlessTurnRate);
                    events.Add(new Translation { Force = steering.Push * mass });
                }
                break;
            case Brake { Guidance: var guidance }:
                Double3 push = guidance.Push(position, velocity, arrivalPoint, center, stationVelocity);
                Face(ref events, push, pose, AttitudeAutopilot.MaxTurnRate);
                Burn(ref events, push, nose, mass);
                break;
            case Glide { Guidance: var guidance }:
                Face(ref events, -axis, pose, AttitudeAutopilot.MaxTurnRate);
                events.Add(new Translation { Force = guidance.Push(position, velocity, holdPoint, center, stationVelocity) * mass });
                break;
            case Hold { Guidance: var guidance }:
                Face(ref events, -axis, pose, AttitudeAutopilot.MaxTurnRate);
                events.Add(new Translation { Force = guidance.Push(position, velocity, holdPoint, center, stationVelocity) * mass });
                break;
        }
    }

    /// <summary>
    /// True when the transfer angle from <paramref name="position"/> to the arrival point of one transfer time later lies
    /// in the plan's window, for the station at <paramref name="center"/>, moving at <paramref name="stationVelocity"/>.
    /// </summary>
    private bool InWindow(TransferGuidance guidance, Double3 position, Double3 center, Double3 stationVelocity, Double3 axis)
    {
        Double3 arrival = guidance.Arrival(center, stationVelocity, axis, plan.ArrivalDistance, plan.TransferTime).Point;
        double angle = TransferGuidance.TransferAngle(position, arrival, TransferGuidance.OrbitNormal(center, stationVelocity));
        return angle >= plan.WindowCloses && angle <= plan.WindowOpens;
    }

    /// <summary>
    /// True when the brake starts: at the arrival time, or in the coast, while the autopilot <paramref name="coasts"/>
    /// (see <see cref="TransferGuidance.Coasts"/>), where the speed that stops the shuttle at the brake's curve in the
    /// <paramref name="distance"/> left to the arrival point is no more than the <paramref name="speed"/> relative to the
    /// station. Before the coast, the shuttle is far from the station, where the relative speed far exceeds the braking
    /// curve's, so the curve says nothing there.
    /// </summary>
    private static bool BrakeStarts(Transfer transfer, double time, bool coasts, double distance, double speed) =>
        transfer.ArrivalTime - time <= 0
            || coasts && Math.Sqrt(2 * ApproachGuidance.ForBrake(transfer.Guidance.Mu).BrakeAcceleration * distance) <= speed;

    /// <summary>
    /// Adds the attitude target that points the nose along <paramref name="nose"/>, with the roof away from the planet, or
    /// that holds the rotation of <paramref name="pose"/> when the nose has no direction, at most at
    /// <paramref name="maxTurnRate"/>, in radians per second.
    /// </summary>
    private static void Face(scoped ref EventList events, Double3 nose, in DPose pose, double maxTurnRate) =>
        events.Add(new AttitudeTarget { Rotation = nose.Length > 1e-9 ? AttitudeAutopilot.Facing(nose, pose) : pose.Rotation, MaxTurnRate = maxTurnRate });

    /// <summary>
    /// Adds the throttle and the translation that give the hull of <paramref name="mass"/> kilograms the acceleration
    /// <paramref name="push"/>: the main engine gives the part along the <paramref name="nose"/>, never below 0 and at most
    /// full throttle, and the translation thrusters the rest.
    /// </summary>
    private void Burn(scoped ref EventList events, Double3 push, Double3 nose, double mass)
    {
        float throttle = (float)Math.Min(1, Math.Max(0, Double3.Dot(push, nose)) * mass / maxThrust);
        events.Add(new Throttle { Value = throttle });
        events.Add(new Translation { Force = push * mass - nose * (throttle * maxThrust) });
    }

    private void Halt(Self self, string reason)
    {
        State = new Halted();
        self.Log($"{nameof(RendezvousAutopilot)} halts: {reason}.");
    }
}
