using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>
/// Worlds whose physics systems start small, for the tests of what happens when a system fills up and grows
/// (see <see cref="JoltBackendOptions.MaxBodiesPerFrame"/>). With a small limit, a few crates fill a system.
/// </summary>
public static class WorldTestingGrowth
{
    /// <summary>
    /// Like <see cref="WorldTesting.NewWorldWithFaults"/>, but the backend creates every frame's physics system with room
    /// for <paramref name="maxBodiesPerFrame"/> bodies, ghosts and character bodies included, and doubles a full system
    /// when it needs one more. The backend writes a warning with "so it grew" to <paramref name="warnings"/> each time it
    /// doubles a system. <paramref name="configure"/> changes any other option of the world before it is created.
    /// </summary>
    public static World NewWorldWithFaultsAndSmallSystems(
        int maxBodiesPerFrame,
        out FaultInjectingBackend faults,
        List<string>? warnings = null,
        bool debugChecks = true,
        Action<WorldOptions>? configure = null
    )
    {
        Action<string>? log = warnings is null
            ? null
            : message =>
            {
                lock (warnings)
                {
                    warnings.Add(message);
                }
            };
        faults = new(new(JoltBackendOptions.Unpaced with { MaxBodiesPerFrame = maxBodiesPerFrame, Log = log }));
        var options = new WorldOptions
        {
            MaxDegreeOfParallelism = 1,
            InitialCapacity = WorldTesting.TestCapacity,
            DebugChecks = debugChecks,
            DisposePhysics = true,
        };
        configure?.Invoke(options);
        return new(options, faults);
    }
}
