using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Walks a person out of a docked ship, down a chute in a spoke and onto the wheel's floor of a spinning station, step 6
/// of the Blue Danube demo (docs/plans/blue-danube-demo.md, "The exit"). It is a flat state machine: its cases are
/// <see cref="DisembarkState"/>, its events <see cref="DisembarkEvent"/>, and <see cref="DisembarkState.Next"/> is the
/// whole table of its changes. Its entity needs a character body.
/// <list type="bullet">
/// <item><see cref="Aboard"/>: it waits for <see cref="LeaveShip"/>.</item>
/// <item><see cref="Walking"/>: it walks the way out with the walker's law (<see cref="Walker.Step"/>), at the walk speed
/// and on its last leg, into the chute's top, at the step-in speed. It reports <see cref="Dropped"/> once the person is off
/// its feet where the chute holds it.</item>
/// <item><see cref="Sliding"/>: it asks for no walk and adds the chute's friction (<see cref="Chute.Friction"/>), and
/// reports <see cref="Landed"/> once the person stands beyond the ceiling's face toward the axis, where the brake run
/// starts.</item>
/// <item><see cref="Strolling"/>: it walks the stroll, and reports <see cref="WalkDone"/> at its last waypoint.</item>
/// <item><see cref="Arrived"/> and <see cref="Halted"/>: it adds nothing.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>A tick applies the messages of the inbox to <see cref="DisembarkState.Next"/> first, in the inbox's order, then
/// flies the delegate of the state after them, then applies the delegate's report. So the walk starts in the tick of the
/// order, and a new case's delegate flies from the next tick. A pair without a row changes nothing and is logged.</para>
/// <para>Every read and write names the station's frame, whichever frame holds the person: the person stays in the
/// cabin's frame until its center leaves the shuttle's bubble in the spoke. A dead station's frame makes the delegate report
/// <see cref="StationGone"/>, with one log line.</para>
/// </remarks>
/// <param name="station">The station's frame.</param>
/// <param name="chute">The chute in the station's axes.</param>
/// <param name="wayOut">The waypoints from the seat to the chute's top.</param>
/// <param name="stroll">The waypoints on the wheel's floor.</param>
/// <param name="walkSpeed">The speed of both walks, in m/s, but for the way out's last leg.</param>
/// <param name="stepInSpeed">The speed of the way out's last leg, into the chute's top, in m/s.</param>
/// <param name="radius">The person's radius, in meters, which the chute's friction law takes.</param>
public sealed class Disembark(FrameHandle station, Chute chute, IReadOnlyList<Waypoint> wayOut, IReadOnlyList<Waypoint> stroll, double walkSpeed,
    double stepInSpeed, double radius) : IComponent
{
    private readonly FrameHandle station = station;
    private readonly Chute chute = chute;
    private readonly Waypoint[] wayOut = [.. wayOut], stroll = [.. stroll];
    private readonly double walkSpeed = walkSpeed, stepInSpeed = stepInSpeed, radius = radius;

    /// <summary>What the exit does now. It starts <see cref="Aboard"/>.</summary>
    public DisembarkState State { get; private set; } = new Aboard();

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>().And<LeaveShip>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        for (int i = 0; i < events.Count; i++)
            if (events[i].Is<LeaveShip>())
                Apply(self, new LeaveShip());
        DisembarkEvent? report = null;
        switch (State)
        {
            case Walking walking:
                report = WalkOut(self, ref walking);
                State = walking;
                break;
            case Sliding:
                report = Slide(self);
                break;
            case Strolling strolling:
                report = Stroll(self, ref strolling);
                State = strolling;
                break;
        }
        Apply(self, report);
    }

    /// <summary>
    /// Walks the way out, and reports <see cref="Dropped"/> once the person is off its feet where the chute holds it, as
    /// it sinks into the chute's top. A walking case keeps its walk, which this delegate updates.
    /// </summary>
    private DisembarkEvent? WalkOut(Self self, ref Walking walking)
    {
        if (!self.Snapshot.Frames.IsAlive(station))
            return Gone(self);
        if (!self.Body.Standing && chute.Holds(self.PoseIn(station).Position))
            return new Dropped();
        WalkProgress before = walking.Walk;
        WalkProgress walk = Walker.Step(self, wayOut, before, SpeedToward(before.Next));
        // A walker that reaches a waypoint walks toward the next one in the same tick, at the speed that it was given. So
        // when that tick starts the last leg, the walk is asked again at the last leg's speed, from the pace before the
        // first. The second walk replaces the first, and the waypoints that it skips are the same.
        if (walk.Next != before.Next && walk.Next < wayOut.Length && SpeedToward(walk.Next) != SpeedToward(before.Next))
            walk = Walker.Step(self, wayOut, before with { Next = walk.Next }, SpeedToward(walk.Next));
        walking = new Walking(walk);
        return null;
    }

    /// <summary>The speed of the walk toward the way out's waypoint at <paramref name="index"/>: the step-in speed on the last leg, the walk speed before it.</summary>
    private double SpeedToward(int index) => index == wayOut.Length - 1 ? stepInSpeed : walkSpeed;

    /// <summary>
    /// Asks for no walk, so that physics keeps no walk for the landing, and adds the chute's friction as a force on the
    /// person's body. Reports <see cref="Landed"/> once the person stands beyond the ceiling's face toward the axis.
    /// </summary>
    private DisembarkEvent? Slide(Self self)
    {
        FrameTree frames = self.Snapshot.Frames;
        if (!frames.IsAlive(station))
            return Gone(self);
        self.Body.MoveCharacter(default);
        Double3 position = self.PoseIn(station).Position;
        if (self.Body.Standing && Math.Sqrt(position.X * position.X + position.Z * position.Z) > -chute.BrakeFrom)
            return new Landed();
        // The station's angular velocity relative to the virtual root, in its own axes, is the opposite of the virtual
        // root's relative to the station.
        Double3 omega = -frames.AngularVelocityRelative(FrameHandle.Null, station);
        Double3 friction = chute.Friction(position, self.VelocityIn(station), omega, radius, self.DeltaTime);
        if (friction == Double3.Zero)
            return null;
        float mass = self.Snapshot.TryGetBody(self.Entity, out BodyFacts body) ? body.Mass : 0;
        self.Body.AddForce((frames.Relative(station, self.Frame).Rotation.Rotate(friction) * mass).ToVector3());
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
        strolling = new Strolling(Walker.Step(self, stroll, strolling.Walk, walkSpeed));
        return strolling.Walk.Next >= stroll.Length ? new WalkDone() : null;
    }

    private static DisembarkEvent Gone(Self self)
    {
        self.Log("Disembark halts: the station's frame is gone.");
        return new StationGone();
    }

    private void Apply(Self self, DisembarkEvent? change)
    {
        if (change is not { } happened)
            return;
        if (DisembarkState.Next(State, happened) is { } next)
            State = next;
        else
            self.Log($"Disembark ignores {happened} in {State}.");
    }
}
