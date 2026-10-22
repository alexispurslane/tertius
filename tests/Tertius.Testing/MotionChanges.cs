using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>
/// Changes of a hull's motion for tests, made the way a game makes them: the entity's own components write
/// <see cref="BodyHandle.SetMotion"/> in a tick, and may swap their rails components in the same run
/// (docs/plans/rails-components.md, "Changes of motion").
/// </summary>
public static class MotionChanges
{
    /// <summary>
    /// Makes <paramref name="owner"/> change the motion of its hull to <paramref name="motion"/> in the next tick, outside a
    /// tick only. A one-shot component runs in that tick: it removes itself and <paramref name="remove"/>, adds
    /// <paramref name="add"/>, and calls <c>Self.Body.SetMotion</c>. Physics gives the Jolt body the new motion at the start
    /// of the step after that tick, and the frame tree stores it at the sync point after that step. A refusal of
    /// <c>SetMotion</c> lands in <see cref="World.Errors"/>, as any exception of a component does.
    /// </summary>
    public static void SetMotionNextTick(this World world, Entity owner, BodyMotion motion, IComponent? add = null,
        IComponent? remove = null) =>
        world.AddComponent(owner, new SetsMotionOnce(motion, add, remove));

    /// <summary>Runs once: it removes itself first, so that a refusal does not make it run again.</summary>
    private sealed class SetsMotionOnce(BodyMotion motion, IComponent? add, IComponent? remove) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            self.RemoveComponent(this);
            if (remove is not null)
                self.RemoveComponent(remove);
            if (add is not null)
                self.AddComponent(add);
            self.Body.SetMotion(motion);
        }
    }
}
