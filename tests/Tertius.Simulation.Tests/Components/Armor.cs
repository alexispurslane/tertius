namespace Tertius.Simulation.Tests;

/// <summary>
/// Takes a flat amount off each <see cref="Damage"/> in the event list, and removes a Damage that nothing is left of,
/// so that a <see cref="Health"/> after it never sees that Damage. The default priority of -10 puts it before
/// <see cref="Health"/>, which runs at 0.
/// </summary>
/// <param name="rating">The amount that it takes off each Damage.</param>
/// <param name="priority">Its place in the entity's pipeline. Lower runs first.</param>
public sealed class Armor(float rating = 1, int priority = -10) : IComponent
{
    public int Priority => priority;
    public EventMask Wants => EventMask.Of<Damage>();

    /// <summary>The total amount of damage that it has taken off so far.</summary>
    public float Absorbed { get; private set; }

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        foreach (ref Damage damage in events.Of<Damage>())
        {
            float taken = MathF.Min(damage.Amount, rating);
            damage.Amount -= taken;
            Absorbed += taken;
        }

        events.Of<Damage>().Where(static (ref damage) => damage.Amount <= 0).Remove();
    }
}
