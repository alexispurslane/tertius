using System.Numerics;
using System.Runtime.CompilerServices;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>A <see cref="Passenger"/> has not reached its seat yet, and its belt is open.</summary>
public readonly record struct Boarding;

/// <summary>A <see cref="Passenger"/> is at its seat with its belt shut.</summary>
/// <param name="WeightlessTicks">How many ticks in a row it has been weightless at its seat, up to the last one.</param>
public readonly record struct Seated(int WeightlessTicks);

/// <summary>A <see cref="Passenger"/> has left its seat and floats to the pen.</summary>
public readonly record struct Fetching;

/// <summary>A <see cref="Passenger"/> holds the pen, and stops where it is.</summary>
/// <param name="Copy">The drawn copy of the pen in its hand.</param>
public readonly record struct Holding(Entity Copy);

/// <summary>The pen went before a <see cref="Passenger"/> took it, so the passenger stops where it is, with empty hands.</summary>
public readonly record struct Stopped;

/// <summary>
/// What a <see cref="Passenger"/> does, in the order of a flight: <see cref="Boarding"/>, <see cref="Seated"/>,
/// <see cref="Fetching"/>, and then <see cref="Holding"/> or <see cref="Stopped"/>. The default value is
/// <see cref="Boarding"/>. This is a custom C# 15 union, like <see cref="AscentState"/>.
/// </summary>
[Union]
public readonly struct PassengerState : IEquatable<PassengerState>
{
    private const byte KindBoarding = 0, KindSeated = 1, KindFetching = 2, KindHolding = 3, KindStopped = 4;

    // A seated state fills the weightless ticks, and a holding one the copy. Every other field is zero, so equality
    // compares every field.
    private readonly Entity copy;
    private readonly int weightlessTicks;
    private readonly byte kind;

    public PassengerState(Boarding boarding) { kind = KindBoarding; }
    public PassengerState(Seated seated) { kind = KindSeated; weightlessTicks = seated.WeightlessTicks; }
    public PassengerState(Fetching fetching) { kind = KindFetching; }
    public PassengerState(Holding holding) { kind = KindHolding; copy = holding.Copy; }
    public PassengerState(Stopped stopped) { kind = KindStopped; }

    /// <summary>Always true: every value is a state, and the default value is <see cref="Boarding"/>.</summary>
    public bool HasValue => true;

    public bool TryGetValue(out Boarding value)
    {
        value = default;
        return kind == KindBoarding;
    }

    public bool TryGetValue(out Seated value)
    {
        value = new Seated(weightlessTicks);
        return kind == KindSeated;
    }

    public bool TryGetValue(out Fetching value)
    {
        value = default;
        return kind == KindFetching;
    }

    public bool TryGetValue(out Holding value)
    {
        value = new Holding(copy);
        return kind == KindHolding;
    }

    public bool TryGetValue(out Stopped value)
    {
        value = default;
        return kind == KindStopped;
    }

    public object Value => kind switch
    {
        KindSeated => new Seated(weightlessTicks),
        KindFetching => new Fetching(),
        KindHolding => new Holding(copy),
        KindStopped => new Stopped(),
        _ => new Boarding(),
    };

    public bool Equals(PassengerState other) => kind == other.kind && weightlessTicks == other.weightlessTicks && copy == other.copy;
    public override bool Equals(object? obj) => obj is PassengerState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, weightlessTicks, copy);
    public static bool operator ==(PassengerState a, PassengerState b) => a.Equals(b);
    public static bool operator !=(PassengerState a, PassengerState b) => !a.Equals(b);

    public override string ToString() => $"PassengerState({Value})";
}

/// <summary>
/// A passenger who rides out the climb at its seat, and in orbit floats to a loose pen and takes it. Its entity needs a
/// character body and a view offset, whose eye the pen in its hand follows (see <see cref="HeldInView"/>). It goes through
/// the cases of <see cref="PassengerState"/>:
/// <list type="bullet">
/// <item><see cref="Boarding"/>: once its center comes within <see cref="BuckleRadius"/> of <paramref name="seat"/>, in
/// the seat's frame, it shuts its belt. Something else, such as a <see cref="Walker"/>, brings it there.</item>
/// <item><see cref="Seated"/>: the belt holds it at the seat, as a seat holds a person who sits in it, whenever the
/// effective gravity at it would not keep it on its feet: when it is weightless, below
/// <see cref="FrameTree.ZeroGravityThreshold"/>, or when it leans more than <see cref="BeltAngle"/> from the seat frame's
/// down, -Y. Then the belt pushes the passenger with its mass times the opposite of that gravity, so that it moves with the
/// cabin, plus a spring and a damper toward the seat, which take out what the push misses. Otherwise the passenger stands
/// on the floor, and the belt does nothing, so it never lifts a person off its feet. Once the passenger has been
/// weightless at its seat for <see cref="UnbuckleSeconds"/>, it opens the belt.</item>
/// <item><see cref="Fetching"/>: its thrusters, as weak as those of <see cref="PersonControl"/>, float it so that its
/// hand, at <see cref="PenHold"/> in its view, in front of its chest, comes onto the pen. The hand first lines up behind
/// the pen, on the line ahead of the view, and then closes in along that line, so that the pen comes to the hand from in
/// front and never touches the body (see <see cref="LineUpGap"/>). Once the hand is within <see cref="GripReach"/> of the
/// pen, the passenger uses the pen (<see cref="Use"/>), and the pen offers itself. The passenger takes the offer as
/// <see cref="Carry"/> takes a crate: it tells the pen that it is taken, so that the pen goes, and it holds a drawn copy
/// of the pen in its hand, where and as the loose pen was, relative to its view. If the pen goes in another way, the
/// passenger stops.</item>
/// <item><see cref="Holding"/> and <see cref="Stopped"/>: its thrusters stop it relative to its frame.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>The passenger reads its own state at the end of the last finished step, and its pushes act in the step of the
/// next tick, so it acts two steps late. The belt's spring and damper are slow for that: the spring swings at
/// <see cref="BeltFrequency"/>, damped critically, about 0.2 radians of phase in the lag. The push against the gravity
/// lags by one step behind the gravity that physics uses, so a sudden change of the cabin's motion moves the passenger,
/// and the spring brings it back: a few centimeters when the engine starts, and about 0.17 m when the hull's tail reaches
/// the foot of the launch ramp and the hull stops turning onto the incline at once.</para>
/// <para>The belt's gate is the lean of the gravity, not physics' report that the person stands, because that report is a
/// step late too. A push in the step in which the person stands again would throw it up off the floor. A character stands
/// on ground up to 50 degrees steep (<c>JoltBackendOptions.CharacterMaxSlopeDegrees</c>), so below the belt's 45 degrees
/// the floor holds the person.</para>
/// <para>The passenger floats in straight lines and does not plan a way round the pen: a character does not turn, so the
/// straight way from its seat to the place where its hand lines up must pass clear of the pen. In the demo, the person
/// floats face-down, below the pen and well to the side of it, so that way passes clear of the pen.</para>
/// <para>The copy in the hand becomes live at the sync point of the tick of the take, and the loose pen goes one tick
/// later, when its <see cref="CrateTaken"/> arrives. So one snapshot holds both, within millimeters of each other, because
/// the passenger floats with the pen when it takes it.</para>
/// </remarks>
/// <param name="seat">Where the passenger's center sits at its seat, in the seat's frame.</param>
/// <param name="pen">The loose pen, a body with a <see cref="Crate"/> component.</param>
public sealed class Passenger(Waypoint seat, Entity pen) : IComponent
{
    /// <summary>How near its seat the passenger's center must come for it to shut its belt, in meters.</summary>
    public const double BuckleRadius = 0.1;

    /// <summary>The frequency at which the belt's spring would swing the passenger without its damper, in radians per second.</summary>
    public const double BeltFrequency = 6;

    /// <summary>How far the effective gravity can lean from the seat's down before the belt holds, in radians: 45 degrees.</summary>
    public const double BeltAngle = Math.PI / 4;

    /// <summary>How long the passenger stays weightless at its seat before it opens its belt, in seconds.</summary>
    public const double UnbuckleSeconds = 2;

    /// <summary>The fastest speed relative to the pen at which the passenger floats to it, in m/s.</summary>
    public const double FloatSpeed = 1;

    /// <summary>
    /// The speed at which the hand floats to the place where it lines up, per meter of the way that is left, in 1/s, up to
    /// <see cref="FloatSpeed"/>.
    /// </summary>
    public const double ApproachRate = 1;

    /// <summary>
    /// How far behind the pen the hand lines up before it closes in, in meters, along the line ahead of the view: the hand
    /// floats first to the place from which the pen lies this far straight ahead of it. The body lies behind the hand, so
    /// the pen stays in front of the body.
    /// </summary>
    public const double LineUpGap = 0.5;

    /// <summary>
    /// How much of the line-up gap the hand keeps per meter that the pen lies off the line ahead of the hand: the gap is
    /// this times that distance, at most <see cref="LineUpGap"/>. So the hand closes in on the pen only as it comes onto the
    /// line.
    /// </summary>
    public const double LineUpSlope = 2;

    /// <summary>How near the hand must come to the pen's center for the passenger to use the pen, in meters.</summary>
    public const double GripReach = 0.02;

    /// <summary>The time in which the thrusters bring the passenger to the velocity that it wants, in seconds.</summary>
    public const double SteerTime = 0.5;

    /// <summary>
    /// Where the passenger's hand takes the pen, from the eye, in meters, in the view's axes: 0.15 m to the right, 0.45 m
    /// down, in front of the chest, and 0.40 m ahead, so that the near end of a pen 14 cm long that points ahead lies 3 cm
    /// in front of the front of a capsule 0.3 m in radius, give or take <see cref="GripReach"/>. A pen held as far out as a
    /// crate (<see cref="Carry.Hold"/>) reads as a pen that floats free.
    /// </summary>
    public static readonly Vector3 PenHold = new(0.15f, -0.45f, -0.40f);

    private readonly Waypoint seat = seat;
    private readonly Entity pen = pen;

    /// <summary>What the passenger does now. It starts <see cref="Boarding"/>.</summary>
    public PassengerState State { get; private set; } = new Boarding();

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>().And<CrateOffer>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        float mass = self.Snapshot.TryGetBody(self.Entity, out BodyFacts body) ? body.Mass : 0;
        switch (State)
        {
            case Boarding:
                if (self.Frame == seat.Frame && Double3.Distance(self.PoseIn(seat.Frame).Position, seat.Position) <= BuckleRadius)
                    State = new Seated(0);
                return;
            case Seated { WeightlessTicks: int ticks }:
                if (self.Frame != seat.Frame)
                    return;
                // The gravity of the last finished step at the seat: the frame's field less its proper acceleration (see Clip).
                FrameTree frames = self.Snapshot.Frames;
                Double3 gravity = frames.Gravity(seat.Frame).AtPrecise(self.PoseIn(seat.Frame).Position) - (Double3)frames.ProperAcceleration(seat.Frame);
                bool weightless = gravity.Length < FrameTree.ZeroGravityThreshold;
                if (weightless || Double3.Dot(gravity, -Double3.UnitY) < gravity.Length * Math.Cos(BeltAngle))
                    Belt(self, gravity, mass);
                ticks = weightless ? ticks + 1 : 0;
                State = ticks * (double)self.DeltaTime >= UnbuckleSeconds ? new Fetching() : new Seated(ticks);
                return;
            case Fetching:
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Is<CrateOffer>() && events[i].Sender == pen && Take(self, outbox, events[i].As<CrateOffer>().Crate))
                    {
                        Steer(self, Double3.Zero, mass);
                        return;
                    }
                }
                Fetch(self, outbox, mass);
                return;
            default:
                Steer(self, Double3.Zero, mass);
                return;
        }
    }

    /// <summary>
    /// Pushes the passenger with the opposite of <paramref name="gravity"/>, the effective gravity at it, plus a spring and
    /// a damper toward its seat, all times its <paramref name="mass"/>.
    /// </summary>
    private void Belt(Self self, Double3 gravity, float mass)
    {
        Double3 offset = seat.Position - self.PoseIn(seat.Frame).Position;
        Double3 push = -gravity + offset * (BeltFrequency * BeltFrequency) - self.VelocityIn(seat.Frame) * (2 * BeltFrequency);
        self.Body.AddForce((push * mass).ToVector3());
    }

    /// <summary>
    /// Floats the hand to the place where it lines up behind the pen, and onto the pen as it comes onto the line, and uses
    /// the pen once the hand is within <see cref="GripReach"/> of it. If the pen is gone, the passenger stops.
    /// </summary>
    private void Fetch(Self self, Outbox outbox, float mass)
    {
        if (!self.Snapshot.TryGetPoseIn(pen, self.Frame, out DPose penPose) || !self.Snapshot.TryGetVelocityIn(pen, self.Frame, out Double3 penVelocity))
        {
            State = new Stopped();
            Steer(self, Double3.Zero, mass);
            return;
        }
        // The hand and the line ahead of the view, in the passenger's frame.
        DPose pose = self.PoseIn(self.Frame);
        Pose view = self.Render.ViewOffset;
        Double3 hand = pose.TransformPoint((Double3)view.TransformPoint(PenHold));
        Double3 ahead = pose.Rotation.Rotate((Double3)view.TransformDirection(-Vector3.UnitZ));
        Double3 way = penPose.Position - hand;
        if (way.Length <= GripReach)
            outbox.Send(pen, new Use { User = self.Entity });
        double along = Double3.Dot(way, ahead);
        double gap = Math.Min(LineUpGap, LineUpSlope * (way - ahead * along).Length);
        // From the hand to the place where it lines up, which slides onto the pen as the hand comes onto the line.
        Double3 lineUp = way - ahead * gap;
        double distance = lineUp.Length;
        double speed = Math.Min(FloatSpeed, distance * ApproachRate);
        Steer(self, penVelocity + (distance > 0 ? lineUp * (speed / distance) : Double3.Zero), mass);
    }

    /// <summary>
    /// Takes the pen that offers itself as <paramref name="offered"/>, as <see cref="Carry"/> takes a crate, and holds a
    /// drawn copy of it where and as the snapshot shows the loose pen, relative to the view. False, with nothing taken, when
    /// the pen is gone by now.
    /// </summary>
    private bool Take(Self self, Outbox outbox, CrateSpec offered)
    {
        if (!self.Snapshot.TryGetPoseIn(pen, self.Frame, out DPose penPose))
            return false;
        Pose view = self.Render.ViewOffset;
        Pose grip = Pose.Compose(view.Inverse(), DPose.Compose(self.PoseIn(self.Frame).Inverse(), penPose).ToPose());
        outbox.Send(pen, new CrateTaken());
        State = new Holding(self.Spawn(offered.HeldCopy(self.Frame, self.Entity, view, grip)));
        return true;
    }

    /// <summary>
    /// Thrusts toward <paramref name="velocity"/>, relative to the passenger's frame: the force that reaches it in
    /// <see cref="SteerTime"/>, at most <see cref="PersonControl.ThrustForce"/>.
    /// </summary>
    private static void Steer(Self self, Double3 velocity, float mass)
    {
        Double3 force = (velocity - self.VelocityIn(self.Frame)) * (mass / SteerTime);
        if (force.Length > PersonControl.ThrustForce)
            force *= PersonControl.ThrustForce / force.Length;
        self.Body.AddForce(force.ToVector3());
    }
}
