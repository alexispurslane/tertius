using System.Runtime.CompilerServices;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>A <see cref="Disembark"/> waits aboard for <see cref="LeaveShip"/>.</summary>
public readonly record struct Aboard;

/// <summary>A <see cref="Disembark"/> walks the way out of the ship to the chute's top.</summary>
/// <param name="Next">The index of the next waypoint of the way out.</param>
public readonly record struct Walking(int Next);

/// <summary>A <see cref="Disembark"/> slides down the chute, and the chute's friction brakes it.</summary>
public readonly record struct Sliding;

/// <summary>A <see cref="Disembark"/> walks a few steps on the wheel's floor.</summary>
/// <param name="Next">The index of the next waypoint of the stroll.</param>
public readonly record struct Strolling(int Next);

/// <summary>A <see cref="Disembark"/> has walked its stroll, and the exit is over.</summary>
public readonly record struct Arrived;

/// <summary>Tells a person to leave the docked ship: its <see cref="Passenger"/> opens the belt, and its <see cref="Disembark"/> starts the walk.</summary>
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
/// is gone. The default value is <see cref="Aboard"/>. This is a custom C# 15 union, like <see cref="PassengerState"/>, and
/// <see cref="Next"/> is the whole table of its changes (docs/plans/blue-danube-demo.md, "The exit component").
/// </summary>
[Union]
public readonly struct DisembarkState : IEquatable<DisembarkState>
{
    private const byte KindAboard = 0, KindWalking = 1, KindSliding = 2, KindStrolling = 3, KindArrived = 4, KindHalted = 5;

    // A walking or strolling state fills the index of its next waypoint. Every other state leaves it zero, so equality
    // compares every field.
    private readonly int next;
    private readonly byte kind;

    public DisembarkState(Aboard aboard) { kind = KindAboard; }
    public DisembarkState(Walking walking) { kind = KindWalking; next = walking.Next; }
    public DisembarkState(Sliding sliding) { kind = KindSliding; }
    public DisembarkState(Strolling strolling) { kind = KindStrolling; next = strolling.Next; }
    public DisembarkState(Arrived arrived) { kind = KindArrived; }
    public DisembarkState(Halted halted) { kind = KindHalted; }

    public static implicit operator DisembarkState(Aboard value) => new(value);
    public static implicit operator DisembarkState(Walking value) => new(value);
    public static implicit operator DisembarkState(Sliding value) => new(value);
    public static implicit operator DisembarkState(Strolling value) => new(value);
    public static implicit operator DisembarkState(Arrived value) => new(value);
    public static implicit operator DisembarkState(Halted value) => new(value);

    /// <summary>Always true: every value is a state, and the default value is <see cref="Aboard"/>.</summary>
    public bool HasValue => true;

    public bool TryGetValue(out Aboard value) { value = default; return kind == KindAboard; }
    public bool TryGetValue(out Walking value) { value = new Walking(next); return kind == KindWalking; }
    public bool TryGetValue(out Sliding value) { value = default; return kind == KindSliding; }
    public bool TryGetValue(out Strolling value) { value = new Strolling(next); return kind == KindStrolling; }
    public bool TryGetValue(out Arrived value) { value = default; return kind == KindArrived; }
    public bool TryGetValue(out Halted value) { value = default; return kind == KindHalted; }

    public object Value => kind switch
    {
        KindWalking => new Walking(next),
        KindSliding => new Sliding(),
        KindStrolling => new Strolling(next),
        KindArrived => new Arrived(),
        KindHalted => new Halted(),
        _ => new Aboard(),
    };

    /// <summary>
    /// The state that <paramref name="change"/> leads to from <paramref name="state"/>, or null when the pair has no row.
    /// It reads nothing but its two arguments.
    /// </summary>
    public static DisembarkState? Next(DisembarkState state, DisembarkEvent change) => (state, change) switch
    {
        (Aboard, LeaveShip) => new Walking(0),
        (Walking, Dropped) => new Sliding(),
        (Sliding, Landed) => new Strolling(0),
        (Strolling, WalkDone) => new Arrived(),
        (Walking or Sliding or Strolling, StationGone) => new Halted(),
        _ => null,
    };

    public bool Equals(DisembarkState other) => kind == other.kind && next == other.next;
    public override bool Equals(object? obj) => obj is DisembarkState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, next);
    public static bool operator ==(DisembarkState a, DisembarkState b) => a.Equals(b);
    public static bool operator !=(DisembarkState a, DisembarkState b) => !a.Equals(b);

    public override string ToString() => $"DisembarkState({Value})";
}

/// <summary>
/// An event of <see cref="Disembark"/>'s machine: the message <see cref="LeaveShip"/>, or a report of a delegate,
/// <see cref="Dropped"/>, <see cref="Landed"/>, <see cref="WalkDone"/> or <see cref="StationGone"/>. A custom C# 15 union.
/// </summary>
[Union]
public readonly struct DisembarkEvent : IEquatable<DisembarkEvent>
{
    private const byte KindLeaveShip = 0, KindDropped = 1, KindLanded = 2, KindWalkDone = 3, KindStationGone = 4;

    private readonly byte kind;

    public DisembarkEvent(LeaveShip leave) { kind = KindLeaveShip; }
    public DisembarkEvent(Dropped dropped) { kind = KindDropped; }
    public DisembarkEvent(Landed landed) { kind = KindLanded; }
    public DisembarkEvent(WalkDone done) { kind = KindWalkDone; }
    public DisembarkEvent(StationGone gone) { kind = KindStationGone; }

    public static implicit operator DisembarkEvent(LeaveShip value) => new(value);
    public static implicit operator DisembarkEvent(Dropped value) => new(value);
    public static implicit operator DisembarkEvent(Landed value) => new(value);
    public static implicit operator DisembarkEvent(WalkDone value) => new(value);
    public static implicit operator DisembarkEvent(StationGone value) => new(value);

    /// <summary>Always true: every value is an event.</summary>
    public bool HasValue => true;

    public bool TryGetValue(out LeaveShip value) { value = default; return kind == KindLeaveShip; }
    public bool TryGetValue(out Dropped value) { value = default; return kind == KindDropped; }
    public bool TryGetValue(out Landed value) { value = default; return kind == KindLanded; }
    public bool TryGetValue(out WalkDone value) { value = default; return kind == KindWalkDone; }
    public bool TryGetValue(out StationGone value) { value = default; return kind == KindStationGone; }

    public object Value => kind switch
    {
        KindDropped => new Dropped(),
        KindLanded => new Landed(),
        KindWalkDone => new WalkDone(),
        KindStationGone => new StationGone(),
        _ => new LeaveShip(),
    };

    public bool Equals(DisembarkEvent other) => kind == other.kind;
    public override bool Equals(object? obj) => obj is DisembarkEvent other && Equals(other);
    public override int GetHashCode() => kind;
    public static bool operator ==(DisembarkEvent a, DisembarkEvent b) => a.Equals(b);
    public static bool operator !=(DisembarkEvent a, DisembarkEvent b) => !a.Equals(b);

    public override string ToString() => $"DisembarkEvent({Value})";
}
