namespace Tertius.Simulation;

/// <summary>Settings of a <see cref="World"/>. They are read once, when the world is created.</summary>
public sealed class WorldOptions
{
    /// <summary>Ticks per second. The fixed time step is 1 / <see cref="TickRate"/>.</summary>
    public int TickRate { get; set; } = 60;

    /// <summary>Entity slots per gameplay chunk. A chunk is the unit of parallel work and owns its own outbox.</summary>
    public int ChunkSize { get; set; } = 64;

    /// <summary>The most gameplay workers that run at once. -1 means no limit. 1 runs every chunk on the ticking thread.</summary>
    public int MaxDegreeOfParallelism { get; set; } = -1;

    /// <summary>Enables extra consistency checks at the sync point. Violations go to <see cref="World.Errors"/>.</summary>
    public bool DebugChecks { get; set; }

    /// <summary>
    /// Free slots reserved for each chunk at every sync, for spawns during the next tick. A chunk that spawned more
    /// last tick gets twice that many. A chunk that runs out falls back to a global reservation, and that tick's slot
    /// assignment then depends on thread timing.
    /// </summary>
    public int SpawnPoolSize { get; set; } = 8;

    /// <summary>Entity slots to allocate up front. The tables grow at the sync point when needed.</summary>
    public int InitialCapacity { get; set; } = 1024;

    /// <summary>The most entries that <see cref="World.Errors"/> keeps. The oldest entries go first.</summary>
    public int MaxErrors { get; set; } = 1000;

    /// <summary>Simulation time at tick 0, in seconds. Kepler frames use it.</summary>
    public double StartTime { get; set; }

    /// <summary>If true, disposing the world also disposes the physics backend.</summary>
    public bool DisposePhysics { get; set; }

    internal void Validate()
    {
        if (TickRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(TickRate), TickRate, "The tick rate must be positive.");
        if (ChunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(ChunkSize), ChunkSize, "The chunk size must be positive.");
        if (MaxDegreeOfParallelism == 0 || MaxDegreeOfParallelism < -1)
            throw new ArgumentOutOfRangeException(nameof(MaxDegreeOfParallelism), MaxDegreeOfParallelism, "Use -1 or a positive number.");
        if (SpawnPoolSize < 0)
            throw new ArgumentOutOfRangeException(nameof(SpawnPoolSize), SpawnPoolSize, "The spawn pool size cannot be negative.");
        if (InitialCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(InitialCapacity), InitialCapacity, "The capacity cannot be negative.");
        if (MaxErrors < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxErrors), MaxErrors, "The error limit cannot be negative.");
        if (!double.IsFinite(StartTime))
            throw new ArgumentOutOfRangeException(nameof(StartTime), StartTime, "The start time must be finite.");
    }
}
