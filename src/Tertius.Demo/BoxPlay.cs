using System.Diagnostics;
using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>One throw of a <see cref="BoxPlay"/>: the loose box that the person takes, and the way that it throws it.</summary>
/// <param name="Box">The loose box, a body with a <see cref="Crate"/> component.</param>
/// <param name="Aim">The unit direction of the throw, in the station's axes.</param>
public readonly record struct BoxThrow(Entity Box, Double3 Aim);

/// <summary>
/// The coda of the Blue Danube demo on the wheel's floor (docs/plans/blue-danube-demo.md, "The coda"): the person walks
/// into a stack of loose boxes and through it, so that the stack falls, walks to a stand, and throws two loose boxes from
/// it the same way, one spinward and one antispinward. It is a flat state machine: its cases are
/// <see cref="BoxPlayState"/>, its events <see cref="BoxPlayEvent"/>, and <see cref="BoxPlayState.Next"/> is the whole
/// table of its changes. Its entity needs a character body and a view offset.
/// <list type="bullet">
/// <item><see cref="Idle"/>: it waits for <see cref="PlayWithBoxes"/>.</item>
/// <item><see cref="Shoving"/> and <see cref="ToStand"/>: it walks the walk through the stack and the walk to the stand
/// with the walker's law (<see cref="Walker.Step"/>), and reports <see cref="WalkDone"/> at each walk's last waypoint.</item>
/// <item><see cref="Taking"/>: it uses the throw's box (<see cref="Use"/>) in every tick, and takes its
/// <see cref="CrateOffer"/> as <see cref="Carry"/> takes a crate: it tells the box that it is taken, so that the box goes,
/// and holds a drawn copy of it where the throw will start (see <see cref="Carry.ThrowStart"/>). It reports
/// <see cref="Took"/> with the copy, or <see cref="BoxGone"/> for a box that is gone.</item>
/// <item><see cref="Aiming"/>: it stands still with the copy for <see cref="AimTicks"/> ticks, then throws the box along
/// the throw's aim at the throw speed, with <see cref="Carry.Release"/>, and reports <see cref="Thrown"/>. The thrown box is
/// a new box with a <see cref="FlightRecorder"/>, turned so that it lies flat on the facet where the straight line of its
/// release lands it. So it lands on a face and lies flat, not on an edge.</item>
/// <item><see cref="Watching"/>: it stands still for <see cref="WatchTicks"/> ticks while the box flies and lands, and
/// reports <see cref="Watched"/>. Then it takes the next throw's box, or the play is over.</item>
/// <item><see cref="Played"/> and <see cref="Halted"/>: it adds nothing.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>A tick applies the messages of the inbox to <see cref="BoxPlayState.Next"/> first, in the inbox's order, then
/// flies the delegate of the state after them, then applies the delegate's report, as <see cref="Disembark"/> does. A
/// <see cref="CrateOffer"/> from the box of the throw in <see cref="Taking"/> is taken among the messages and reported as
/// <see cref="Took"/>. A pair without a row changes nothing and is logged.</para>
/// <para>Every read names the station's frame. The throws happen in the station's frame, where the person stands on the
/// wheel's floor, so the aims turn into the person's axes by its rotation there. A dead station's frame makes the delegate
/// report <see cref="StationGone"/>, with one log line.</para>
/// </remarks>
/// <param name="station">The station's frame.</param>
/// <param name="shove">The waypoints of the walk into the stack and through it.</param>
/// <param name="toStand">The waypoints of the walk to the stand.</param>
/// <param name="throws">The throws, <see cref="BoxPlayState.Throws"/> of them, in their order.</param>
/// <param name="walkSpeed">The speed of both walks, in m/s.</param>
/// <param name="throwSpeed">The speed of each throw relative to the person, in m/s.</param>
/// <param name="body">The person's capsule, which a thrown box starts clear of.</param>
/// <param name="floor">The station's floor, in its axes.</param>
/// <param name="rate">The station's steady spin rate about its +Y, in rad/s.</param>
public sealed class BoxPlay(
    FrameHandle station,
    IReadOnlyList<Waypoint> shove,
    IReadOnlyList<Waypoint> toStand,
    IReadOnlyList<BoxThrow> throws,
    double walkSpeed,
    float throwSpeed,
    CapsuleShapeDesc body,
    WheelFloor floor,
    double rate) : IComponent
{
    /// <summary>How long the person holds a box still before it throws it, in ticks: half a second.</summary>
    public const int AimTicks = 30;

    /// <summary>
    /// How long the person stands still after a throw, in ticks: 2.5 s, longer than the 1.7 s flight of the antispinward
    /// throw, so that each box lands before the next take.
    /// </summary>
    public const int WatchTicks = 150;

    private readonly FrameHandle station = station;
    private readonly Waypoint[] shove = [.. shove], toStand = [.. toStand];

    private readonly BoxThrow[] throws = throws.Count == BoxPlayState.Throws
        ? [.. throws]
        : throw new ArgumentException($"A box play has {BoxPlayState.Throws} throws, not {throws.Count}.",
            nameof(throws));

    private readonly double walkSpeed = walkSpeed, rate = rate;
    private readonly float throwSpeed = throwSpeed;
    private readonly CapsuleShapeDesc body = body ?? throw new ArgumentNullException(nameof(body));
    private readonly WheelFloor floor = floor;
    private readonly CrateSpec[] taken = new CrateSpec[BoxPlayState.Throws];
    private readonly List<Entity> thrown = [];

    /// <summary>What the play does now. It starts <see cref="Idle"/>.</summary>
    public BoxPlayState State { get; private set; } = new Idle();

    /// <summary>The boxes that the person has thrown, in the order of the throws. Each has a <see cref="FlightRecorder"/>.</summary>
    public IReadOnlyList<Entity> Thrown => thrown;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>().And<PlayWithBoxes>().And<CrateOffer>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        for (int i = 0; i < events.Count; i++)
        {
            ref Event e = ref events[i];
            if (e.Is<PlayWithBoxes>())
                Apply(self, new PlayWithBoxes());
            else if (e.Is<CrateOffer>() && State.TryGetValue(out Taking taking) && e.Sender == throws[taking.Throw].Box)
                Apply(self, Take(self, outbox, taking.Throw, e.Sender, e.As<CrateOffer>().Crate));
        }

        BoxPlayEvent? report = null;
        switch (State)
        {
            case Shoving shoving:
                (report, WalkProgress through) = Walk(self, shove, shoving.Walk);
                State = new Shoving(through);
                break;
            case ToStand walking:
                (report, WalkProgress toward) = Walk(self, toStand, walking.Walk);
                State = new ToStand(toward);
                break;
            case Taking { Throw: var index }:
                report = Use(self, outbox, index);
                break;
            case Aiming aiming:
                report = Aim(self, outbox, ref aiming);
                State = aiming;
                break;
            case Watching watching:
                report = Watch(self, ref watching);
                State = watching;
                break;
            case Idle or Played or Halted or null:
                break;
            default:
                throw new UnreachableException($"The box play state {State} has a case that the play does not run.");
        }

        Apply(self, report);
    }

    /// <summary>
    /// Walks <paramref name="path"/> one tick from <paramref name="walk"/>, and reports <see cref="WalkDone"/> at its last
    /// waypoint. Each walk starts from rest, so the walker's start limit lets the person set off in about a second.
    /// </summary>
    private (BoxPlayEvent? Report, WalkProgress Walk) Walk(Self self, Waypoint[] path, WalkProgress walk)
    {
        if (!self.Snapshot.Frames.IsAlive(station))
            return (Gone(self), walk);
        walk = Walker.Step(self, path, walk, walkSpeed);
        return (walk.Next >= path.Length ? new WalkDone() : null, walk);
    }

    /// <summary>Uses the box of throw <paramref name="index"/>, or reports <see cref="BoxGone"/> for a box that is gone.</summary>
    private BoxPlayEvent? Use(Self self, Outbox outbox, int index)
    {
        if (!self.Snapshot.Frames.IsAlive(station))
            return Gone(self);
        Entity box = throws[index].Box;
        if (!self.Snapshot.IsAlive(box))
        {
            self.Log($"BoxPlay halts: the box of throw {index} is gone.");
            return new BoxGone();
        }

        outbox.Send(box, new Use { User = self.Entity });
        return null;
    }

    /// <summary>
    /// Takes the box <paramref name="box"/> of throw <paramref name="index"/> that offers itself as
    /// <paramref name="offered"/>, and holds its drawn copy where the throw will start, turned as it will start. Null, with
    /// nothing taken, when the box is gone by now.
    /// </summary>
    private BoxPlayEvent? Take(Self self, Outbox outbox, int index, Entity box, CrateSpec offered)
    {
        if (!self.Snapshot.IsAlive(box))
            return null;
        taken[index] = offered;
        Pose view = self.Render.ViewOffset;
        (Vector3 aim, Quaternion turn) = AimInPerson(self, index, offered);
        var grip = Pose.Compose(view.Inverse(), new(Carry.ThrowStart(body, offered, view.Position, aim), turn));
        outbox.Send(box, new CrateTaken());
        return new Took(self.Spawn(offered.HeldCopy(self.Frame, self.Entity, view, grip)));
    }

    /// <summary>
    /// Holds the box still for <see cref="AimTicks"/>, then throws it, and reports <see cref="Thrown"/>. An aiming case
    /// keeps its count of ticks, which this delegate updates.
    /// </summary>
    private BoxPlayEvent? Aim(Self self, Outbox outbox, ref Aiming aiming)
    {
        if (!self.Snapshot.Frames.IsAlive(station))
            return Gone(self);
        aiming = aiming with { Ticks = aiming.Ticks + 1 };
        if (aiming.Ticks < AimTicks)
            return null;
        CrateSpec box = taken[aiming.Throw];
        (Vector3 aim, Quaternion turn) = AimInPerson(self, aiming.Throw, box);
        thrown.Add(Carry.Release(self, outbox, box, aiming.Copy, body, self.Render.ViewOffset.Position, aim, turn,
            throwSpeed,
            new FlightRecorder(station, floor, rate, box.HalfExtents.Y)));
        return new Thrown();
    }

    /// <summary>
    /// Stands still for <see cref="WatchTicks"/> after a throw, and reports <see cref="Watched"/>. A watching case keeps its
    /// count of ticks, which this delegate updates.
    /// </summary>
    private BoxPlayEvent? Watch(Self self, ref Watching watching)
    {
        if (!self.Snapshot.Frames.IsAlive(station))
            return Gone(self);
        watching = watching with { Ticks = watching.Ticks + 1 };
        return watching.Ticks < WatchTicks ? null : new Watched();
    }

    /// <summary>
    /// The aim of throw <paramref name="index"/> and the turn of its box, in the person's axes: the turn lays the box flat on
    /// the facet where the straight line of its release lands it, a release from where <see cref="Carry.ThrowStart"/> puts
    /// it, at the person's velocity plus the throw.
    /// </summary>
    private (Vector3 Aim, Quaternion Turn) AimInPerson(Self self, int index, CrateSpec box)
    {
        DPose person = self.PoseIn(station);
        DQuaternion toPerson = person.Rotation.Conjugate();
        var aim = toPerson.Rotate(throws[index].Aim).ToVector3();
        Double3 start = person.TransformPoint(Carry.ThrowStart(body, box, self.Render.ViewOffset.Position, aim));
        var release = new ThrowRelease(start, self.VelocityIn(station) + throws[index].Aim * throwSpeed);
        int facet = floor.FacetIndex(release.Landing(rate, floor, box.HalfExtents.Y).Place);
        return (aim, (toPerson * floor.FacetTurn(facet)).ToQuaternion());
    }

    private static BoxPlayEvent Gone(Self self)
    {
        self.Log("BoxPlay halts: the station's frame is gone.");
        return new StationGone();
    }

    private void Apply(Self self, BoxPlayEvent? change)
    {
        if (change is not { } happened)
            return;
        if (BoxPlayState.Next(State, happened) is { } next)
            State = next;
        else
            self.Log($"BoxPlay ignores {happened} in {State}.");
    }
}
