namespace Tertius.Physics;

/// <summary>Tuning for <see cref="JoltPhysicsBackend"/>. The defaults suit a 60 Hz space sim.</summary>
public sealed record JoltBackendOptions
{
    // Every frame with bodies gets its own Jolt system, and Jolt sizes its buffers from these limits up front, so
    // they are per frame and kept moderate.

    /// <summary>The most bodies that one frame's physics system can hold, ghosts and character bodies included.</summary>
    public int MaxBodiesPerFrame { get; init; } = 4096;

    /// <summary>The most body pairs that one frame's broad phase can report per step.</summary>
    public int MaxBodyPairsPerFrame { get; init; } = 8192;

    /// <summary>The most contact constraints that one frame's solver can hold per step.</summary>
    public int MaxContactConstraintsPerFrame { get; init; } = 4096;

    /// <summary>Jolt worker threads. Zero or less uses Jolt's default: one per core, minus one.</summary>
    public int WorkerThreads { get; init; }

    /// <summary>
    /// How far bodies may sink into each other before Jolt pushes them apart, in meters. A resting body sinks about
    /// half of this. Jolt's own default is 0.02.
    /// </summary>
    public float PenetrationSlop { get; init; } = 0.005f;

    /// <summary>The speed limit of a body, in m/s. Jolt's own default of 500 m/s is far too low for spacecraft.</summary>
    public float MaxLinearVelocity { get; init; } = 30_000f;

    /// <summary>Below this effective gravity, in m/s², a character switches to zero-g movement.</summary>
    public float ZeroGravityThreshold { get; init; } = 0.05f;

    /// <summary>The steepest slope that a character can stand on, in degrees.</summary>
    public float CharacterMaxSlopeDegrees { get; init; } = 50f;

    /// <summary>The largest force with which a character pushes dynamic bodies, in newtons.</summary>
    public float CharacterMaxStrength { get; init; } = 100f;

    /// <summary>
    /// When the effective gravity of a frame changes by more than this, in m/s², physics wakes every body in the
    /// frame, so that sleeping bodies react to a ship that starts or stops thrusting. It sits above the noise of a
    /// proper acceleration measured from float velocities at orbital speed.
    /// </summary>
    public float WakeAccelerationThreshold { get; init; } = 0.02f;

    /// <summary>Receives warnings. If it is null, warnings go to <see cref="System.Diagnostics.Trace"/>.</summary>
    public Action<string>? Log { get; init; }
}
