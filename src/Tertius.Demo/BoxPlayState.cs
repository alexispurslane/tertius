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
/// <see cref="Halted"/> once the station's frame or a box is gone. The default value is <see cref="Idle"/>. A generated
/// union like <see cref="DisembarkState"/>, and <see cref="Next"/> is the whole table of its changes
/// (docs/plans/blue-danube-demo.md, "The box play component").
/// </summary>
[GeneratedUnion(typeof(Idle), typeof(Shoving), typeof(ToStand), typeof(Taking), typeof(Aiming), typeof(Watching),
    typeof(Played), typeof(Halted), ZeroCase = typeof(Idle))]
public readonly partial struct BoxPlayState
{
    /// <summary>The number of throws: one spinward and one antispinward.</summary>
    public const int Throws = 2;

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
}

/// <summary>
/// An event of <see cref="BoxPlay"/>'s machine: the message <see cref="PlayWithBoxes"/>, or a report,
/// <see cref="WalkDone"/>,
/// <see cref="Took"/>, <see cref="Thrown"/>, <see cref="Watched"/>, <see cref="StationGone"/> or <see cref="BoxGone"/>. A
/// generated union, whose all-zero value is <see cref="PlayWithBoxes"/>.
/// </summary>
[GeneratedUnion(typeof(PlayWithBoxes), typeof(WalkDone), typeof(Took), typeof(Thrown), typeof(Watched),
    typeof(StationGone), typeof(BoxGone), ZeroCase = typeof(PlayWithBoxes))]
public readonly partial struct BoxPlayEvent;
