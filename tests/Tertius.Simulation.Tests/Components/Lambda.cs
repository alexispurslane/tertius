namespace Tertius.Simulation.Tests;

/// <summary>
/// The shape of <see cref="IComponent.Handle"/>. A delegate type of our own, because <see cref="EventList"/> and
/// <see cref="Outbox"/> are ref structs and cannot be type arguments of <see cref="Action{T}"/>. A lambda for it can
/// leave out the parameter types and keep the modifiers: <c>(self, scoped ref events, scoped ref outbox) => ...</c>.
/// </summary>
public delegate void HandleFn(Self self, scoped ref EventList events, scoped ref Outbox outbox);

/// <summary>
/// A component whose behavior is a delegate. Use it for the one-off behavior of a test, when no other component in
/// this folder does the job. It counts its runs, so a test can tell that the scheduler skipped it.
/// </summary>
/// <param name="wants">The event types it handles. The scheduler skips it when none of them is in the list.</param>
/// <param name="handle">What it does each time it runs.</param>
/// <param name="priority">Its place in the entity's pipeline. Lower runs first.</param>
public sealed class Lambda(EventMask wants, HandleFn handle, int priority = 0) : IComponent
{
    public int Priority => priority;
    public EventMask Wants => wants;

    /// <summary>How many times the scheduler has run it.</summary>
    public int Runs;

    /// <summary>A lambda that runs on every tick: it wants <see cref="Tick"/>, which the scheduler sends to every entity that wants it.</summary>
    public static Lambda OnTick(HandleFn handle, int priority = 0) => new(EventMask.Of<Tick>(), handle, priority);

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        Runs++;
        handle(self, ref events, ref outbox);
    }
}
