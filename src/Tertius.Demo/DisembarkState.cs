namespace Tertius.Demo;

/// <summary>A <see cref="Disembark"/> waits aboard for <see cref="LeaveShip"/>.</summary>
public readonly record struct Aboard;

/// <summary>A <see cref="Disembark"/> walks the way out of the ship to the chute's top.</summary>
/// <param name="Walk">How far the walk has come along the way out, and how fast it walks.</param>
public readonly record struct Walking(WalkProgress Walk);

/// <summary>A <see cref="Disembark"/> slides down the chute, and the chute's friction brakes it.</summary>
public readonly record struct Sliding;

/// <summary>A <see cref="Disembark"/> walks a few steps on the wheel's floor.</summary>
/// <param name="Walk">How far the walk has come along the stroll, and how fast it walks.</param>
public readonly record struct Strolling(WalkProgress Walk);

/// <summary>A <see cref="Disembark"/> has walked its stroll, and the exit is over.</summary>
public readonly record struct Arrived;

/// <summary>Tells a person to leave the docked ship: its <see cref="Passenger"/> opens the belt, and its
/// <see cref="Disembark"/> starts the walk.</summary>
public struct LeaveShip : IEventPayload;

/// <summary>A report of <see cref="Disembark"/>'s walk: the person is off its feet over the chute's top.</summary>
public readonly record struct Dropped;

// The report of Disembark's slide that the person stands on the wheel's floor is Landed, which the docking's machine
// reports too. It is defined beside DockingAutopilot.

/// <summary>A report of <see cref="Disembark"/>'s stroll: the walker reached its last waypoint.</summary>
public readonly record struct WalkDone;

/// <summary>A report of any delegate of <see cref="Disembark"/>: the station's frame is gone.</summary>
public readonly record struct StationGone;

/// <summary>
/// What a <see cref="Disembark"/> does, in the order of the exit: <see cref="Aboard"/>, <see cref="Walking"/>,
/// <see cref="Sliding"/>, <see cref="Strolling"/> and <see cref="Arrived"/>, or <see cref="Halted"/> once the station's frame
/// is gone. The default value is <see cref="Aboard"/>. It is a generated union (see <see cref="GeneratedUnionAttribute"/>
/// ), and
/// <see cref="Next"/> is the whole table of its changes (docs/plans/blue-danube-demo.md, "The exit component").
/// </summary>
[GeneratedUnion(typeof(Aboard), typeof(Walking), typeof(Sliding), typeof(Strolling), typeof(Arrived), typeof(Halted),
    ZeroCase = typeof(Aboard))]
public readonly partial struct DisembarkState
{
    /// <summary>
    /// The state that <paramref name="change"/> leads to from <paramref name="state"/>, or null when the pair has no row.
    /// It reads nothing but its two arguments.
    /// </summary>
    public static DisembarkState? Next(DisembarkState state, DisembarkEvent change) => (state, change) switch
    {
        (Aboard, LeaveShip) => new Walking(WalkProgress.Start),
        (Walking, Dropped) => new Sliding(),
        (Sliding, Landed) => new Strolling(WalkProgress.Start),
        (Strolling, WalkDone) => new Arrived(),
        (Walking or Sliding or Strolling, StationGone) => new Halted(),
        _ => null
    };
}

/// <summary>
/// An event of <see cref="Disembark"/>'s machine: the message <see cref="LeaveShip"/>, or a report of a delegate,
/// <see cref="Dropped"/>, <see cref="Landed"/>, <see cref="WalkDone"/> or <see cref="StationGone"/>. It is a generated union, and
/// the all-zero value is <see cref="LeaveShip"/>.
/// </summary>
[GeneratedUnion(typeof(LeaveShip), typeof(Dropped), typeof(Landed), typeof(WalkDone), typeof(StationGone),
    ZeroCase = typeof(LeaveShip))]
public readonly partial struct DisembarkEvent;
