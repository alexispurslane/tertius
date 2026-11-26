namespace Tertius.Demo;

/// <summary>A <see cref="Disembark"/> waits aboard for <see cref="LeaveShip"/>.</summary>
public readonly record struct Aboard;

/// <summary>A <see cref="Disembark"/> walks the way out of the ship onto the lift.</summary>
/// <param name="Walk">How far the walk has come along the way out, and how fast it walks.</param>
public readonly record struct Walking(WalkProgress Walk);

/// <summary>A <see cref="Disembark"/> stands on the lift as it rides down the spoke.</summary>
public readonly record struct Riding;

/// <summary>A <see cref="Disembark"/> walks a few steps off the lift onto the wheel's floor.</summary>
/// <param name="Walk">How far the walk has come along the stroll, and how fast it walks.</param>
public readonly record struct Strolling(WalkProgress Walk);

/// <summary>A <see cref="Disembark"/> has walked its stroll, and the exit is over.</summary>
public readonly record struct Arrived;

/// <summary>Tells a person to leave the docked ship: its <see cref="Passenger"/> opens the belt, and its
/// <see cref="Disembark"/> starts the walk.</summary>
public struct LeaveShip : IEventPayload;

/// <summary>A report of <see cref="Disembark"/>'s walk: the person stopped in the middle of the lift's floor.</summary>
public readonly record struct Boarded;

/// <summary>A report of <see cref="Disembark"/>'s stroll: the walker reached its last waypoint.</summary>
public readonly record struct WalkDone;

/// <summary>A report of any delegate of <see cref="Disembark"/>: the station's frame is gone.</summary>
public readonly record struct StationGone;

/// <summary>
/// What a <see cref="Disembark"/> does, in the order of the exit: <see cref="Aboard"/>, <see cref="Walking"/>,
/// <see cref="Riding"/>, <see cref="Strolling"/> and <see cref="Arrived"/>, or <see cref="Halted"/> once the station's frame
/// is gone. The default value is <see cref="Aboard"/>. It is a generated union (see <see cref="GeneratedUnionAttribute"/>),
/// and <see cref="Next"/> is the whole table of its changes (https://github.com/alexispurslane/tertius/wiki/blue-danube-demo, "The exit component").
/// </summary>
[GeneratedUnion(
    typeof(Aboard),
    typeof(Walking),
    typeof(Riding),
    typeof(Strolling),
    typeof(Arrived),
    typeof(Halted),
    ZeroCase = typeof(Aboard)
)]
public readonly partial struct DisembarkState
{
    /// <summary>
    /// The state that <paramref name="change"/> leads to from <paramref name="state"/>, or null when the pair has no row.
    /// It reads nothing but its two arguments.
    /// </summary>
    public static DisembarkState? Next(DisembarkState state, DisembarkEvent change) =>
        (state, change) switch
        {
            (Aboard, LeaveShip) => new Walking(WalkProgress.Start),
            (Walking, Boarded) => new Riding(),
            (Riding, LiftStopped) => new Strolling(WalkProgress.Start),
            (Strolling, WalkDone) => new Arrived(),
            (Walking or Riding or Strolling, StationGone) => new Halted(),
            _ => null,
        };
}

/// <summary>
/// An event of <see cref="Disembark"/>'s machine: the messages <see cref="LeaveShip"/> and <see cref="LiftStopped"/>, or a
/// report of a delegate, <see cref="Boarded"/>, <see cref="WalkDone"/> or <see cref="StationGone"/>. It is a generated union,
/// and the all-zero value is <see cref="LeaveShip"/>.
/// </summary>
[GeneratedUnion(
    typeof(LeaveShip),
    typeof(Boarded),
    typeof(LiftStopped),
    typeof(WalkDone),
    typeof(StationGone),
    ZeroCase = typeof(LeaveShip)
)]
public readonly partial struct DisembarkEvent;
