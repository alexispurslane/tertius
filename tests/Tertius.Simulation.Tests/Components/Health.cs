namespace Tertius.Simulation.Tests;

/// <summary>
/// Hit points. <see cref="Damage"/> lowers <see cref="Current"/> and <see cref="Heal"/> raises it, up to
/// <see cref="Max"/>. When damage brings <see cref="Current"/> to zero or below, it adds a <see cref="Destroy"/> to the
/// event list, so that an entity with a <see cref="Health"/> dies unless a later component removes that event, as an
/// <see cref="Invulnerable"/> does. It runs at priority 0. <see cref="Armor"/> runs before it and
/// <see cref="Invulnerable"/> after it, with their default priorities.
/// </summary>
/// <remarks>
/// The fields are public and the class has a parameterless constructor, because the blueprint tests build it from a
/// <see cref="ComponentSpec"/>, which sets fields and properties by name. <see cref="Side"/>, <see cref="Name"/> and
/// <see cref="Armor"/> are plain data for those tests and do nothing in <see cref="Handle"/>. <see cref="Armor"/> is
/// not the <see cref="Tertius.Simulation.Tests.Armor"/> component, which is what reduces damage.
/// </remarks>
public sealed class Health : IComponent
{
    /// <summary>The most that <see cref="Heal"/> can restore.</summary>
    public float Max = 100;

    public Faction Side;
    public string? Name;
    public int? Armor;

    /// <summary>The hit points now.</summary>
    public float Current { get; set; } = 100;

    public int Priority => 0;
    public EventMask Wants => EventMask.Of<Damage>().And<Heal>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        bool damaged = false;
        foreach (ref Damage damage in events.Of<Damage>())
        {
            Current -= damage.Amount;
            damaged = true;
        }

        foreach (ref Heal heal in events.Of<Heal>())
            Current = MathF.Min(Max, Current + heal.Amount);

        // Added after the loops: the list must not change while it is enumerated.
        if (damaged && Current <= 0)
            events.Add(new Destroy(), self.Entity);
    }
}
