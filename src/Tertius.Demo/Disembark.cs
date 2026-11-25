using System.Diagnostics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Walks a person out of a docked ship onto a lift, rides it down a spoke and walks onto the wheel's floor of a spinning
/// station, step 6 of the Blue Danube demo (docs/plans/blue-danube-demo.md, "The exit"). It is a flat state machine: its
/// cases are <see cref="DisembarkState"/>, its events <see cref="DisembarkEvent"/>, and <see cref="DisembarkState.Next"/> is
/// the whole table of its changes. Its entity needs a character body.
/// <list type="bullet">
/// <item><see cref="Aboard"/>: it waits for <see cref="LeaveShip"/>.</item>
/// <item><see cref="Walking"/>: it walks the way out with the walker's law (<see cref="Walker.Step"/>), and reports
/// <see cref="Boarded"/> once the person stops at its last waypoint, the middle of the lift's floor. The change into
/// <see cref="Riding"/> sends the lift <see cref="Descend"/>.</item>
/// <item><see cref="Riding"/>: it asks for no walk, so the person stands on the lift, until the lift sends
/// <see cref="LiftStopped"/> from the foot.</item>
/// <item><see cref="Strolling"/>: it walks the stroll, and reports <see cref="WalkDone"/> at its last waypoint.</item>
/// <item><see cref="Arrived"/> and <see cref="Halted"/>: it adds nothing.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>A tick applies the messages of the inbox to <see cref="DisembarkState.Next"/> first, in the inbox's order, then
/// flies the delegate of the state after them, then applies the delegate's report. So the walk starts in the tick of the
/// order, and the stroll in the tick of the lift's stop, and a case that a report starts flies from the next tick. A pair
/// without a row changes nothing and is logged.</para>
/// <para>Every read and write names the station's frame, whichever frame holds the person: the person stays in the
/// cabin's frame until its center leaves the shuttle's bubble in the shaft. A dead station's frame makes the delegate report
/// <see cref="StationGone"/>, with one log line.</para>
/// </remarks>
/// <param name="station">The station's frame.</param>
/// <param name="lift">The lift that takes the person down the spoke.</param>
/// <param name="wayOut">The waypoints from the seat to the middle of the lift's floor.</param>
/// <param name="stroll">The waypoints from the lift's floor at the foot onto the wheel's floor.</param>
/// <param name="walkSpeed">The speed of both walks, in m/s.</param>
public sealed class Disembark(
    FrameHandle station,
    Entity lift,
    IReadOnlyList<Waypoint> wayOut,
    IReadOnlyList<Waypoint> stroll,
    double walkSpeed
) : IComponent
{
    private readonly FrameHandle station = station;
    private readonly Entity lift = lift;
    private readonly Waypoint[] wayOut = [.. wayOut],
        stroll = [.. stroll];
    private readonly double walkSpeed = walkSpeed;

    /// <summary>What the exit does now. It starts <see cref="Aboard"/>.</summary>
    public DisembarkState State { get; private set; } = new Aboard();

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>().And<LeaveShip>().And<LiftStopped>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        for (int i = 0; i < events.Count; i++)
            if (events[i].Is<LeaveShip>())
                Apply(self, ref outbox, new LeaveShip());
            else if (events[i].Is<LiftStopped>())
                Apply(self, ref outbox, new LiftStopped());
        DisembarkEvent? report = null;
        switch (State)
        {
            case Walking walking:
                report = WalkOut(self, ref walking);
                State = walking;
                break;
            case Riding:
                report = Ride(self);
                break;
            case Strolling strolling:
                report = Stroll(self, ref strolling);
                State = strolling;
                break;
            case Aboard or Arrived or Halted:
                break;
            default:
                throw new UnreachableException($"The disembark state {State} has a case that the walk does not run.");
        }

        if (report is { } happened)
            Apply(self, ref outbox, happened);
    }

    /// <summary>
    /// Walks the way out, and reports <see cref="Boarded"/> once the walker stops at its last waypoint, the middle of the
    /// lift's floor. A walking case keeps its walk, which this delegate updates.
    /// </summary>
    private DisembarkEvent? WalkOut(Self self, ref Walking walking)
    {
        if (!self.Snapshot.Frames.IsAlive(station))
            return Gone(self);
        walking = new(Walker.Step(self: self, path: wayOut, walk: walking.Walk, speed: walkSpeed));
        return walking.Walk.Next >= wayOut.Length ? new Boarded() : null;
    }

    /// <summary>Asks for no walk, so the person stands still on the lift's floor as the lift rides.</summary>
    private DisembarkEvent? Ride(Self self)
    {
        if (!self.Snapshot.Frames.IsAlive(station))
            return Gone(self);
        self.Body.MoveCharacter(default);
        return null;
    }

    /// <summary>
    /// Walks the stroll at the walk speed, and reports <see cref="WalkDone"/> once the walker stops at its last waypoint.
    /// A strolling case keeps its walk, which this delegate updates.
    /// </summary>
    private DisembarkEvent? Stroll(Self self, ref Strolling strolling)
    {
        if (!self.Snapshot.Frames.IsAlive(station))
            return Gone(self);
        strolling = new(Walker.Step(self: self, path: stroll, walk: strolling.Walk, speed: walkSpeed));
        return strolling.Walk.Next >= stroll.Length ? new WalkDone() : null;
    }

    private static DisembarkEvent Gone(Self self)
    {
        self.Log("Disembark halts: the station's frame is gone.");
        return new StationGone();
    }

    /// <summary>
    /// Changes the case by <see cref="DisembarkState.Next"/>, and does the side effect of the change: from
    /// <see cref="Walking"/> into <see cref="Riding"/>, the person orders the lift down.
    /// </summary>
    private void Apply(Self self, ref Outbox outbox, DisembarkEvent change)
    {
        DisembarkState old = State;
        if (DisembarkState.Next(old, change) is not { } next)
        {
            self.Log($"Disembark ignores {change} in {old}.");
            return;
        }

        State = next;
        if (old is Walking && next is Riding)
            outbox.Send(lift, new Descend());
    }
}
