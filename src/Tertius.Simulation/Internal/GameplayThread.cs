namespace Tertius.Simulation;

/// <summary>
/// The thread-static marker that the scheduler sets around each entity run. Code that must not run inside a
/// component's <see cref="IComponent.Handle"/> call, such as building the heap part of an entity query, tests it.
/// </summary>
internal static class GameplayThread
{
    /// <summary>True while this thread runs an entity's components.</summary>
    [field: ThreadStatic]
    public static bool InEntityRun { get; private set; }

    /// <summary>Sets the marker for one entity run. The scheduler calls it, and <see cref="EndEntityRun"/> in a finally block.</summary>
    public static void BeginEntityRun()
    {
        Contract.Requires(!InEntityRun);
        InEntityRun = true;
    }

    public static void EndEntityRun() => InEntityRun = false;
}
