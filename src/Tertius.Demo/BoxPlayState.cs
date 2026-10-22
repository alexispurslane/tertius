using System.Runtime.CompilerServices;

namespace Tertius.Demo;

/// <summary>A <see cref="BoxPlay"/> waits on the wheel's floor for <see cref="PlayWithBoxes"/>.</summary>
public readonly record struct Idle;

/// <summary>A <see cref="BoxPlay"/> walks into the stack of boxes and through it, so that the stack falls.</summary>
/// <param name="Walk">How far the walk has come along the walk through the stack, and how fast it walks.</param>
public readonly record struct Shoving(WalkProgress Walk);

/// <summary>A <see cref="BoxPlay"/> walks to the stand from which it throws.</summary>
/// <param name="Walk">How far the walk has come along the walk to the stand, and how fast it walks.</param>
public readonly record struct ToStand(WalkProgress Walk);

/// <summary>A <see cref="BoxPlay"/> uses the box of a throw at the stand, and waits for the box's offer.</summary>
/// <param name="Throw">The index of the throw, 0 for the first and 1 for the second.</param>
public readonly record struct Taking(int Throw);

/// <summary>A <see cref="BoxPlay"/> holds the box of a throw at the place where the throw starts, and stands still.</summary>
/// <param name="Throw">The index of the throw.</param>
/// <param name="Copy">The drawn copy of the box in the person's hands.</param>
/// <param name="Ticks">How many ticks it has held the box so far.</param>
public readonly record struct Aiming(int Throw, Entity Copy, int Ticks);

/// <summary>A <see cref="BoxPlay"/> stands still after a throw and watches the box fly and land.</summary>
/// <param name="Throw">The index of the throw.</param>
/// <param name="Ticks">How many ticks it has watched so far.</param>
public readonly record struct Watching(int Throw, int Ticks);

/// <summary>A <see cref="BoxPlay"/> has thrown both boxes and watched them land, and the play is over.</summary>
public readonly record struct Played;

/// <summary>Tells a person on the wheel's floor to put the pen away and play with the loose boxes: its
/// <see cref="Passenger"/> puts the pen away, and its <see cref="BoxPlay"/> starts.</summary>
public struct PlayWithBoxes : IEventPayload;

/// <summary>A report of <see cref="BoxPlay"/>: it took the offered box, and holds <paramref name="Copy"/>, the box's drawn copy.</summary>
/// <param name="Copy">The drawn copy of the box in the person's hands.</param>
public readonly record struct Took(Entity Copy);

/// <summary>A report of <see cref="BoxPlay"/>: it threw the box that it held.</summary>
public readonly record struct Thrown;

/// <summary>A report of <see cref="BoxPlay"/>: it has watched a throw for as long as it watches one.</summary>
public readonly record struct Watched;

/// <summary>A report of <see cref="BoxPlay"/>: the box of a throw is gone before the person took it.</summary>
public readonly record struct BoxGone;

/// <summary>
/// What a <see cref="BoxPlay"/> does, in the order of the coda: <see cref="Idle"/>, <see cref="Shoving"/>,
/// <see cref="ToStand"/>, then <see cref="Taking"/>, <see cref="Aiming"/> and <see cref="Watching"/> for each throw, and
/// <see cref="Played"/>, or
/// <see cref="Halted"/> once the station's frame or a box is gone. The default value is <see cref="Idle"/>. A custom C# 15
/// union like <see cref="DisembarkState"/>, and <see cref="Next"/> is the whole table of its changes
/// (docs/plans/blue-danube-demo.md, "The box play component").
/// </summary>
[Union]
public readonly struct BoxPlayState : IEquatable<BoxPlayState>
{
    /// <summary>The number of throws: one spinward and one antispinward.</summary>
    public const int Throws = 2;

    private const byte KindIdle = 0,
        KindShoving = 1,
        KindToStand = 2,
        KindTaking = 3,
        KindAiming = 4,
        KindWatching = 5,
        KindPlayed = 6,
        KindHalted = 7;

    // A walking state fills its walk, a taking state the index of its throw, an aiming state its throw, its copy and its
    // ticks, and a watching state its throw and its ticks. Every other field is zero, so equality compares every field.
    private readonly WalkProgress walk;
    private readonly Entity copy;
    private readonly int number, ticks;
    private readonly byte kind;

    public BoxPlayState(Idle idle)
    {
        kind = KindIdle;
    }

    public BoxPlayState(Shoving shoving)
    {
        kind = KindShoving;
        walk = shoving.Walk;
    }

    public BoxPlayState(ToStand toStand)
    {
        kind = KindToStand;
        walk = toStand.Walk;
    }

    public BoxPlayState(Taking taking)
    {
        kind = KindTaking;
        number = taking.Throw;
    }

    public BoxPlayState(Aiming aiming)
    {
        kind = KindAiming;
        number = aiming.Throw;
        copy = aiming.Copy;
        ticks = aiming.Ticks;
    }

    public BoxPlayState(Watching watching)
    {
        kind = KindWatching;
        number = watching.Throw;
        ticks = watching.Ticks;
    }

    public BoxPlayState(Played played)
    {
        kind = KindPlayed;
    }

    public BoxPlayState(Halted halted)
    {
        kind = KindHalted;
    }

    /// <summary>Always true: every value is a state, and the default value is <see cref="Idle"/>.</summary>
    public bool HasValue => true;

    public object Value => kind switch
    {
        KindShoving => new Shoving(walk),
        KindToStand => new ToStand(walk),
        KindTaking => new Taking(number),
        KindAiming => new Aiming(number, copy, ticks),
        KindWatching => new Watching(number, ticks),
        KindPlayed => new Played(),
        KindHalted => new Halted(),
        _ => new Idle()
    };

    public static implicit operator BoxPlayState(Idle value) => new(value);
    public static implicit operator BoxPlayState(Shoving value) => new(value);
    public static implicit operator BoxPlayState(ToStand value) => new(value);
    public static implicit operator BoxPlayState(Taking value) => new(value);
    public static implicit operator BoxPlayState(Aiming value) => new(value);
    public static implicit operator BoxPlayState(Watching value) => new(value);
    public static implicit operator BoxPlayState(Played value) => new(value);
    public static implicit operator BoxPlayState(Halted value) => new(value);

    public bool TryGetValue(out Idle value)
    {
        value = default;
        return kind == KindIdle;
    }

    public bool TryGetValue(out Shoving value)
    {
        value = new(walk);
        return kind == KindShoving;
    }

    public bool TryGetValue(out ToStand value)
    {
        value = new(walk);
        return kind == KindToStand;
    }

    public bool TryGetValue(out Taking value)
    {
        value = new(number);
        return kind == KindTaking;
    }

    public bool TryGetValue(out Aiming value)
    {
        value = new(number, copy, ticks);
        return kind == KindAiming;
    }

    public bool TryGetValue(out Watching value)
    {
        value = new(number, ticks);
        return kind == KindWatching;
    }

    public bool TryGetValue(out Played value)
    {
        value = default;
        return kind == KindPlayed;
    }

    public bool TryGetValue(out Halted value)
    {
        value = default;
        return kind == KindHalted;
    }

    /// <summary>
    /// The state that <paramref name="change"/> leads to from <paramref name="state"/>, or null when the pair has no row.
    /// It reads nothing but its two arguments.
    /// </summary>
    public static BoxPlayState? Next(BoxPlayState state, BoxPlayEvent change) => (state, change) switch
    {
        (Idle, PlayWithBoxes) => new Shoving(WalkProgress.Start),
        (Shoving, WalkDone) => new ToStand(WalkProgress.Start),
        (ToStand, WalkDone) => new Taking(0),
        (Taking { Throw: var index }, Took { Copy: var held }) => new Aiming(index, held, 0),
        (Aiming { Throw: var index }, Thrown) => new Watching(index, 0),
        (Watching { Throw: var index }, Watched) when index + 1 < Throws => new Taking(index + 1),
        (Watching, Watched) => new Played(),
        (Shoving or ToStand or Taking or Aiming or Watching, StationGone) => new Halted(),
        (Taking, BoxGone) => new Halted(),
        _ => null
    };

    public bool Equals(BoxPlayState other) => kind == other.kind && walk == other.walk && number == other.number &&
                                              ticks == other.ticks && copy == other.copy;

    public override bool Equals(object? obj) => obj is BoxPlayState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, walk, number, ticks, copy);
    public static bool operator ==(BoxPlayState a, BoxPlayState b) => a.Equals(b);
    public static bool operator !=(BoxPlayState a, BoxPlayState b) => !a.Equals(b);

    public override string ToString() => $"BoxPlayState({Value})";
}

/// <summary>
/// An event of <see cref="BoxPlay"/>'s machine: the message <see cref="PlayWithBoxes"/>, or a report,
/// <see cref="WalkDone"/>,
/// <see cref="Took"/>, <see cref="Thrown"/>, <see cref="Watched"/>, <see cref="StationGone"/> or <see cref="BoxGone"/>. A
/// custom C# 15 union.
/// </summary>
[Union]
public readonly struct BoxPlayEvent : IEquatable<BoxPlayEvent>
{
    private const byte KindPlay = 0,
        KindWalkDone = 1,
        KindTook = 2,
        KindThrown = 3,
        KindWatched = 4,
        KindStationGone = 5,
        KindBoxGone = 6;

    // A Took fills the copy. Every other event leaves it null.
    private readonly Entity copy;
    private readonly byte kind;

    public BoxPlayEvent(PlayWithBoxes play)
    {
        kind = KindPlay;
    }

    public BoxPlayEvent(WalkDone done)
    {
        kind = KindWalkDone;
    }

    public BoxPlayEvent(Took took)
    {
        kind = KindTook;
        copy = took.Copy;
    }

    public BoxPlayEvent(Thrown thrown)
    {
        kind = KindThrown;
    }

    public BoxPlayEvent(Watched watched)
    {
        kind = KindWatched;
    }

    public BoxPlayEvent(StationGone gone)
    {
        kind = KindStationGone;
    }

    public BoxPlayEvent(BoxGone gone)
    {
        kind = KindBoxGone;
    }

    /// <summary>Always true: every value is an event.</summary>
    public bool HasValue => true;

    public object Value => kind switch
    {
        KindWalkDone => new WalkDone(),
        KindTook => new Took(copy),
        KindThrown => new Thrown(),
        KindWatched => new Watched(),
        KindStationGone => new StationGone(),
        KindBoxGone => new BoxGone(),
        _ => new PlayWithBoxes()
    };

    public static implicit operator BoxPlayEvent(PlayWithBoxes value) => new(value);
    public static implicit operator BoxPlayEvent(WalkDone value) => new(value);
    public static implicit operator BoxPlayEvent(Took value) => new(value);
    public static implicit operator BoxPlayEvent(Thrown value) => new(value);
    public static implicit operator BoxPlayEvent(Watched value) => new(value);
    public static implicit operator BoxPlayEvent(StationGone value) => new(value);
    public static implicit operator BoxPlayEvent(BoxGone value) => new(value);

    public bool TryGetValue(out PlayWithBoxes value)
    {
        value = default;
        return kind == KindPlay;
    }

    public bool TryGetValue(out WalkDone value)
    {
        value = default;
        return kind == KindWalkDone;
    }

    public bool TryGetValue(out Took value)
    {
        value = new(copy);
        return kind == KindTook;
    }

    public bool TryGetValue(out Thrown value)
    {
        value = default;
        return kind == KindThrown;
    }

    public bool TryGetValue(out Watched value)
    {
        value = default;
        return kind == KindWatched;
    }

    public bool TryGetValue(out StationGone value)
    {
        value = default;
        return kind == KindStationGone;
    }

    public bool TryGetValue(out BoxGone value)
    {
        value = default;
        return kind == KindBoxGone;
    }

    public bool Equals(BoxPlayEvent other) => kind == other.kind && copy == other.copy;
    public override bool Equals(object? obj) => obj is BoxPlayEvent other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, copy);
    public static bool operator ==(BoxPlayEvent a, BoxPlayEvent b) => a.Equals(b);
    public static bool operator !=(BoxPlayEvent a, BoxPlayEvent b) => !a.Equals(b);

    public override string ToString() => $"BoxPlayEvent({Value})";
}
