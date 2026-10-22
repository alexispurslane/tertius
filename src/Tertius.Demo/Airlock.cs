using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Cycles an airlock's two doors, so that one of them is always shut. When the airlock starts, it opens the inner door,
/// or the outer door when <paramref name="outerOpenAtStart"/> is true. A <see cref="Use"/> starts a cycle: the airlock
/// tells the open door to close, and <paramref name="cycleSeconds"/> later it tells the other door to open. A use during
/// a cycle does nothing. The airlock's position is where a person must reach to use it.
/// </summary>
/// <remarks>
/// The doors are <see cref="Door"/> entities that spawn shut, except that the outer door of an airlock that starts open to
/// the outside may spawn open, so that it does not move at the start. The airlock talks to them only by messages. For the doors
/// never to stand open together, <paramref name="cycleSeconds"/> must be at least the time that a door takes to close.
/// </remarks>
/// <param name="innerDoor">The door between the airlock and the inside.</param>
/// <param name="outerDoor">The door between the airlock and the outside.</param>
/// <param name="cycleSeconds">
/// The time from the order that closes one door to the order that opens the other, in seconds. It must be positive and
/// finite, or the constructor throws <see cref="ArgumentOutOfRangeException"/>. A cycle lasts at least one tick.
/// </param>
/// <param name="outerOpenAtStart">
/// True for an airlock that opens to the outside when it starts, such as one that lets cargo drift out. A use then
/// cycles it to the inner door.
/// </param>
public sealed class Airlock(Entity innerDoor, Entity outerDoor, float cycleSeconds, bool outerOpenAtStart = false)
    : IComponent
{
    private readonly float cycleSeconds = cycleSeconds > 0 && float.IsFinite(cycleSeconds)
        ? cycleSeconds
        : throw new ArgumentOutOfRangeException(nameof(cycleSeconds), cycleSeconds,
            "The cycle time must be positive and finite.");

    // True while the outer door is the open one, or the one that the current cycle opens.
    private bool outerOpen = outerOpenAtStart;

    // The first tick at which a use starts a new cycle. By then the open order of the last cycle reached its door, so a
    // new close order never reaches that door in the same tick as the open order.
    private long cycleEnd;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Use>().And<Spawned>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        for (int i = 0; i < events.Count; i++)
            if (events[i].Is<Spawned>())
            {
                outbox.Send(outerOpen ? outerDoor : innerDoor, new OpenDoor());
            }
            else if (events[i].Is<Use>() && self.Tick >= cycleEnd)
            {
                // The open order joins the routing cycleTicks ticks after the close order does. A delayed send waits at
                // least one tick.
                int cycleTicks = Math.Max(1, (int)MathF.Round(cycleSeconds / self.DeltaTime));
                outbox.Send(outerOpen ? outerDoor : innerDoor, new CloseDoor());
                outerOpen = !outerOpen;
                outbox.SendAfter(cycleTicks, outerOpen ? outerDoor : innerDoor, new OpenDoor());
                cycleEnd = self.Tick + cycleTicks + 1;
            }
    }
}
