using System.Diagnostics.CodeAnalysis;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The base of the flag components: components with no state and no behavior, which other entities ask about through
/// the snapshot. It handles no events, so the scheduler never runs it.
/// </summary>
[SuppressMessage("Tertius.Unions", "TU0004",
    Justification =
        "An open set: flags are components, and the set of components is open by design. Other entities ask for a flag by its type, never through a switch over the flags.")]
public abstract class Flag : IComponent
{
    public int Priority => 0;
    public EventMask Wants => default;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
    }
}

/// <summary>A flag: the entity burns.</summary>
public sealed class Flammable : Flag;

/// <summary>A flag: the entity is wet, so it does not burn.</summary>
public sealed class Wet : Flag;

/// <summary>A flag: the entity is metal.</summary>
public sealed class Metal : Flag;

/// <summary>A flag: the entity is crew.</summary>
public sealed class Crew : Flag;

/// <summary>A flag: the entity is dead.</summary>
public sealed class Dead : Flag;
