namespace Tertius.Simulation.Tests;

/// <summary>
/// Spawns entities from inside a tick, through <see cref="Self.Spawn(SpawnDesc)"/>. Each child is created in the
/// spawner's frame at the spawner's pose, with no body and no render row, and it becomes live at the sync point of
/// the tick that spawned it. For a child that needs more than components (a body, an attachment, a frame), use a
/// <see cref="Lambda"/> and build the <see cref="SpawnDesc"/> there.
/// </summary>
/// <param name="childComponents">
/// Makes the components of one child. It runs once per child, because a component instance belongs to one entity.
/// Null means children with no components.
/// </param>
/// <param name="count">How many children it spawns each time it spawns.</param>
/// <param name="onTick">The first tick on which it spawns. Its default of 0 means the first tick the entity runs.</param>
/// <param name="every">The number of ticks between two spawns. 0 means it spawns once.</param>
/// <param name="priority">Its place in the entity's pipeline. Lower runs first.</param>
public sealed class Spawner(Func<IComponent[]>? childComponents = null, int count = 1, long onTick = 0, int every = 0, int priority = 0) : IComponent
{
    public int Priority => priority;
    public EventMask Wants => EventMask.Of<Tick>();

    /// <summary>The handle of every child it has spawned, in spawn order. A child is live from the tick after it was spawned.</summary>
    public List<Entity> Children { get; } = new();

    private bool spawnedOnce;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (!SpawnsOn(self.Tick))
            return;
        spawnedOnce = true;
        for (int i = 0; i < count; i++)
        {
            Children.Add(self.Spawn(new SpawnDesc
            {
                Frame = self.Frame,
                Pose = self.Pose,
                Components = childComponents?.Invoke() ?? [],
            }));
        }
    }

    // A spawner with every == 0 spawns on the first tick it runs at or after onTick. Testing the tick number and not a
    // count of runs keeps a spawner that starts late on the same schedule as one that started early.
    private bool SpawnsOn(long tick)
    {
        if (tick < onTick)
            return false;
        return every > 0 ? (tick - onTick) % every == 0 : !spawnedOnce;
    }
}
