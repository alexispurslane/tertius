using System.Diagnostics;
using Tertius.Frames.Rails;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>Tells a <see cref="Lift"/> at the top to take its rider, the sender, down the spoke.</summary>
public struct Descend : IEventPayload;

/// <summary>Tells the rider that sent a <see cref="Lift"/> its <see cref="Descend"/> that the lift stopped at the foot.</summary>
public struct LiftStopped : IEventPayload;

/// <summary>A <see cref="Lift"/> rests at the top of its spoke and waits for <see cref="Descend"/>.</summary>
public readonly record struct AtTop;

/// <summary>A <see cref="Lift"/> rides down its spoke on its profile.</summary>
/// <param name="Start">The world time at which the ride started, in seconds: the start of the first step that it moved in.</param>
/// <param name="Rider">The entity that ordered the ride, which hears <see cref="LiftStopped"/> at the foot.</param>
public readonly record struct Descending(double Start, Entity Rider);

/// <summary>A <see cref="Lift"/> rests at the foot of its spoke.</summary>
public readonly record struct AtFoot;

/// <summary>An event of a <see cref="Lift"/>'s machine: the order to descend, from <paramref name="Start"/>, for <paramref name="Rider"/>.</summary>
/// <param name="Start">The world time at which the ride starts, in seconds.</param>
/// <param name="Rider">The entity that sent the order.</param>
public readonly record struct LiftOrder(double Start, Entity Rider);

/// <summary>An event of a <see cref="Lift"/>'s machine: the ride's profile is over, and the lift rests at the foot.</summary>
public readonly record struct ReachedFoot;

/// <summary>
/// What a <see cref="Lift"/> does: <see cref="AtTop"/>, <see cref="Descending"/> or <see cref="AtFoot"/>, in that order. The
/// default value is <see cref="AtTop"/>. <see cref="Next"/> is the whole table of its changes.
/// </summary>
[GeneratedUnion(typeof(AtTop), typeof(Descending), typeof(AtFoot), ZeroCase = typeof(AtTop))]
public readonly partial struct LiftState
{
    /// <summary>
    /// The state that <paramref name="change"/> leads to from <paramref name="state"/>, or null when the pair has no row: an
    /// order at the top starts the descent, and the end of the profile stops it at the foot. It reads nothing but its two
    /// arguments.
    /// </summary>
    public static LiftState? Next(LiftState state, LiftEvent change) =>
        (state, change) switch
        {
            (AtTop, LiftOrder { Start: var start, Rider: var rider }) => new Descending(Start: start, Rider: rider),
            (Descending, ReachedFoot) => new AtFoot(),
            _ => null,
        };
}

/// <summary>An event of a <see cref="Lift"/>'s machine: <see cref="LiftOrder"/> or <see cref="ReachedFoot"/>. The default value is <see cref="LiftOrder"/>.</summary>
[GeneratedUnion(typeof(LiftOrder), typeof(ReachedFoot), ZeroCase = typeof(LiftOrder))]
public readonly partial struct LiftEvent;

/// <summary>
/// A lift's platform, a kinematic plain body that rides down a spoke of a spinning station on <paramref name="profile"/>
/// (docs/plans/blue-danube-demo.md, "The lift"). It is a flat state machine: its cases are <see cref="LiftState"/>, and
/// <see cref="LiftState.Next"/> is the whole table of its changes. At the top it holds still. A <see cref="Descend"/> starts
/// the ride at the start of the next step. In each tick of the ride it writes the profile's full sample of that step, from
/// <paramref name="top"/> along <paramref name="outward"/>, and at the profile's end it stops and sends
/// <see cref="LiftStopped"/> to the rider. A pair without a row changes nothing and is logged.
/// </summary>
/// <remarks>
/// Every place is in <paramref name="station"/>'s axes. A bubble that the platform touches, such as a docked ship's, can
/// hold it in another frame. That frame must rest in the station's, as a parked ship's does, and the lift turns its sample
/// into that frame's axes by their fixed relation. When the station's frame is gone, the way is gone, and the lift holds
/// still where it is.
/// </remarks>
/// <param name="station">The station's frame, in whose axes the ride runs.</param>
/// <param name="top">The platform's pose at the top, in the station's axes, where it spawns.</param>
/// <param name="outward">The unit direction of the ride, away from the axis along the spoke, in the station's axes.</param>
/// <param name="profile">The ride's profile.</param>
public sealed class Lift(FrameHandle station, DPose top, Double3 outward, LiftProfile profile) : IComponent
{
    private readonly FrameHandle station = station;
    private readonly DPose top = top;
    private readonly Double3 outward = outward;
    private readonly LiftProfile profile = profile;

    /// <summary>What the lift does now. It starts <see cref="AtTop"/>.</summary>
    public LiftState State { get; private set; } = new AtTop();

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>().And<Descend>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        // The step that applies this tick's motion intent starts at the next tick's time and ends one step later.
        double stepStart = self.TimeAt(self.Tick + 1),
            stepEnd = self.TimeAt(self.Tick + 2);
        for (int i = 0; i < events.Count; i++)
            if (events[i].Is<Descend>())
                Apply(self, ref outbox, new LiftOrder(Start: stepStart, Rider: events[i].Sender));
        switch (State)
        {
            case Descending { Start: var start }:
                if (Ride(self, stepEnd - start) is { } report)
                    Apply(self, ref outbox, report);
                break;
            case AtTop or AtFoot:
                break;
            default:
                throw new UnreachableException($"The lift state {State} has a case that the ride does not run.");
        }
    }

    /// <summary>
    /// Writes the profile's sample of the step that ends <paramref name="elapsed"/> seconds into the ride, in the axes of
    /// the frame that holds the lift, and reports <see cref="ReachedFoot"/> from the step that ends the profile.
    /// </summary>
    private ReachedFoot? Ride(Self self, double elapsed)
    {
        FrameTree frames = self.Snapshot.Frames;
        if (!frames.IsAlive(station))
            return null;
        MotionSample sample = profile.Sample(top: top, outward: outward, elapsed: elapsed, step: self.DeltaTime);
        self.MoveOnRails(self.Frame == station ? sample : Into(frames.Relative(station, self.Frame), sample));
        return elapsed >= profile.Duration ? new ReachedFoot() : null;
    }

    /// <summary>
    /// <paramref name="sample"/>, in the station's axes, in the axes of a frame that rests in the station's, whose relation
    /// to it is <paramref name="station"/>, the station frame's pose there.
    /// </summary>
    private static MotionSample Into(in DPose station, in MotionSample sample) =>
        sample with
        {
            Pose = DPose.Compose(station, sample.Pose),
            ChordVelocity = station.Rotation.Rotate(sample.ChordVelocity),
            Velocity = station.Rotation.Rotate(sample.Velocity),
            Acceleration = station.Rotation.Rotate(sample.Acceleration),
        };

    /// <summary>
    /// Changes the case by <see cref="LiftState.Next"/>, and does the side effect of the change: into <see cref="AtFoot"/>,
    /// the lift tells its rider that it stopped.
    /// </summary>
    private void Apply(Self self, ref Outbox outbox, LiftEvent change)
    {
        LiftState old = State;
        if (LiftState.Next(old, change) is not { } next)
        {
            self.Log($"Lift ignores {change} in {old}.");
            return;
        }

        State = next;
        if (old is Descending { Rider: var rider } && next is AtFoot && !rider.IsNull)
            outbox.Send(rider, new LiftStopped());
    }
}
