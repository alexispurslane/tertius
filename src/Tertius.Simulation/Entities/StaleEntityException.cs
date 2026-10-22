using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Tertius.Simulation;

/// <summary>
/// A stale or null entity handle was used where a live entity is needed. A handle is stale when its slot holds no live
/// entity, or holds a newer entity than the one the handle was made for. Every member that reads or writes an entity's
/// data checks the handle on every call, in every build, and throws this for a stale one. It is an
/// <see cref="InvalidOperationException"/>, the type that the world threw for a stale handle before, so code that
/// catches that type still works.
/// </summary>
public sealed class StaleEntityException(Entity entity)
    : InvalidOperationException($"{entity} is stale or null: the entity is not live.")
{
    /// <summary>The handle that was used.</summary>
    public Entity Entity { get; } = entity;

    /// <summary>Throws for <paramref name="entity"/>. Out of line, so that the checks on the hot paths stay small.</summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void Throw(Entity entity) => throw new StaleEntityException(entity);
}
