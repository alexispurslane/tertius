using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Tells an entity that a person within reach uses it, as when the person presses E. What a use does is up to the
/// entity's components: an <see cref="Airlock"/> cycles. Most entities do not want it, and the router drops it for them.
/// </summary>
public struct Use : IEventPayload
{
    /// <summary>The entity that uses the recipient.</summary>
    public Entity User;
}
