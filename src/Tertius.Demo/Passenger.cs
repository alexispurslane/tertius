using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>What a <see cref="Passenger"/> does, in the order of a flight.</summary>
public enum PassengerPhase
{
    /// <summary>The passenger has not reached its seat yet, and its belt is open.</summary>
    Boarding,

    /// <summary>The passenger is at its seat with its belt shut.</summary>
    Seated,

    /// <summary>The passenger has left its seat and floats to the pen.</summary>
    Fetching,

    /// <summary>The passenger holds the pen, and stops where it is.</summary>
    Holding,
}

/// <summary>
/// A passenger who rides out the climb at its seat, and in orbit floats to a loose pen and takes it. Its entity needs a
/// character body and a view offset, whose eye the pen in its hand follows (see <see cref="HeldInView"/>).
/// <list type="bullet">
/// <item><see cref="PassengerPhase.Boarding"/>: once its center comes within <see cref="BuckleRadius"/> of
/// <paramref name="seat"/>, in the seat's frame, it shuts its belt. Something else, such as a <see cref="Walker"/>, brings
/// it there.</item>
/// <item><see cref="PassengerPhase.Seated"/>: the belt holds it at the seat, as a seat holds a person who sits in it,
/// whenever the effective gravity at it would not keep it on its feet: when it is weightless, below
/// <see cref="FrameTree.ZeroGravityThreshold"/>, or when it leans more than <see cref="BeltAngle"/> from the seat frame's
/// down, -Y. Then the belt pushes the passenger with its mass times the opposite of that gravity, so that it moves with the
/// cabin, plus a spring and a damper toward the seat, which take out what the push misses. Otherwise the passenger stands
/// on the floor, and the belt does nothing, so it never lifts a person off its feet. Once the passenger has been
/// weightless at its seat for <see cref="UnbuckleSeconds"/>, it opens the belt.</item>
/// <item><see cref="PassengerPhase.Fetching"/>: its thrusters, as weak as those of <see cref="PersonControl"/>, push it
/// toward the pen, until it floats with the pen at <see cref="ApproachDistance"/> from it. Within <see cref="GrabReach"/>
/// it uses the pen (<see cref="Use"/>), and the pen offers itself. The passenger takes the offer as <see cref="Carry"/>
/// takes a crate: it tells the pen that it is taken, so that the pen goes, and it holds a drawn copy of the pen. If the pen
/// goes in another way, the passenger stops.</item>
/// <item><see cref="PassengerPhase.Holding"/>: its thrusters stop it relative to its frame.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>The passenger reads its own state at the end of the last finished step, and its pushes act in the step of the
/// next tick, so it acts two steps late. The belt's spring and damper are slow for that: the spring swings at
/// <see cref="BeltFrequency"/>, damped critically, about 0.2 radians of phase in the lag. The push against the gravity
/// lags by one step behind the gravity that physics uses, so a sudden change of thrust moves the passenger a few
/// centimeters, and the spring brings it back.</para>
/// <para>The belt's gate is the lean of the gravity, not physics' report that the person stands, because that report is a
/// step late too. A push in the step in which the person stands again would throw it up off the floor. A character stands
/// on ground up to 50 degrees steep (<c>JoltBackendOptions.CharacterMaxSlopeDegrees</c>), so below the belt's 45 degrees
/// the floor holds the person.</para>
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

    /// <summary>How far from the pen the passenger floats to, center to center, in meters.</summary>
    public const double ApproachDistance = 1;

    /// <summary>How far from the passenger's center a pen can be for it to take it, in meters: about an arm's reach from the hips.</summary>
    public const double GrabReach = 1.2;

    /// <summary>The time in which the thrusters bring the passenger to the velocity that it wants, in seconds.</summary>
    public const double SteerTime = 0.5;

    private readonly Waypoint seat = seat;
    private readonly Entity pen = pen;

    // How many ticks in a row the passenger has been weightless at its seat.
    private int weightlessTicks;

    /// <summary>What the passenger does now.</summary>
    public PassengerPhase Phase { get; private set; } = PassengerPhase.Boarding;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>().And<CrateOffer>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        float mass = self.Snapshot.TryGetBody(self.Entity, out BodyFacts body) ? body.Mass : 0;
        switch (Phase)
        {
            case PassengerPhase.Boarding:
                if (self.Frame == seat.Frame && Double3.Distance(self.PoseIn(seat.Frame).Position, seat.Position) <= BuckleRadius)
                    Phase = PassengerPhase.Seated;
                return;
            case PassengerPhase.Seated:
                if (self.Frame != seat.Frame)
                    return;
                Double3 gravity = self.Snapshot.Frames.EffectiveGravity(seat.Frame, self.PoseIn(seat.Frame).Position);
                bool weightless = gravity.Length < FrameTree.ZeroGravityThreshold;
                if (weightless || Double3.Dot(gravity, -Double3.UnitY) < gravity.Length * Math.Cos(BeltAngle))
                    Belt(self, gravity, mass);
                weightlessTicks = weightless ? weightlessTicks + 1 : 0;
                if (weightlessTicks * (double)self.DeltaTime >= UnbuckleSeconds)
                    Phase = PassengerPhase.Fetching;
                return;
            case PassengerPhase.Fetching:
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Is<CrateOffer>() && events[i].Sender == pen)
                    {
                        Take(self, outbox, events[i].As<CrateOffer>().Crate);
                        goto case PassengerPhase.Holding;
                    }
                }
                Fetch(self, outbox, mass);
                return;
            case PassengerPhase.Holding:
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
    /// Floats to the pen until the passenger floats with it at <see cref="ApproachDistance"/>, and uses it within
    /// <see cref="GrabReach"/>. If the pen is gone, the passenger stops.
    /// </summary>
    private void Fetch(Self self, Outbox outbox, float mass)
    {
        if (!self.Snapshot.TryGetPoseIn(pen, self.Frame, out DPose penPose) || !self.Snapshot.TryGetVelocityIn(pen, self.Frame, out Double3 penVelocity))
        {
            Phase = PassengerPhase.Holding;
            Steer(self, Double3.Zero, mass);
            return;
        }
        Double3 way = penPose.Position - self.PoseIn(self.Frame).Position;
        double distance = way.Length;
        if (distance <= GrabReach)
            outbox.Send(pen, new Use { User = self.Entity });
        // The speed toward the pen falls with the distance that is left past the approach distance, 1 m/s per meter.
        double speed = Math.Clamp(distance - ApproachDistance, 0, FloatSpeed);
        Steer(self, penVelocity + (distance > 0 ? way * (speed / distance) : Double3.Zero), mass);
    }

    /// <summary>Takes the pen that offers itself as <paramref name="offered"/>, as <see cref="Carry"/> takes a crate.</summary>
    private void Take(Self self, Outbox outbox, CrateSpec offered)
    {
        outbox.Send(pen, new CrateTaken());
        self.Spawn(offered.HeldCopy(self.Frame, self.Entity, self.Render.ViewOffset));
        Phase = PassengerPhase.Holding;
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
