namespace Tertius.Simulation;

/// <summary>
/// The thread-static marker that the scheduler sets around each entity run. Code that must not run inside a
/// component's <see cref="IComponent.Handle"/> call, such as building the heap part of an entity query, tests it.
/// </summary>
internal static class GameplayThread
{
    [ThreadStatic]
    private static bool inEntityRun;

    /// <summary>True while this thread runs an entity's components.</summary>
    public static bool InEntityRun => inEntityRun;

    /// <summary>Sets the marker for one entity run. The scheduler calls it, and <see cref="EndEntityRun"/> in a finally block.</summary>
    public static void BeginEntityRun()
    {
        Contract.Requires(!inEntityRun);
        inEntityRun = true;
    }

    public static void EndEntityRun() => inEntityRun = false;
}
