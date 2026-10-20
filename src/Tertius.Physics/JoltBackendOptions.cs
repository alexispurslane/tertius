namespace Tertius.Physics;

/// <summary>Tuning for <see cref="JoltPhysicsBackend"/>. The defaults suit a 60 Hz space sim.</summary>
public sealed record JoltBackendOptions
{
    // Every frame with bodies gets its own Jolt system, and Jolt sizes its buffers from these limits up front, so
    // they are per frame and kept moderate.

    /// <summary>
    /// The most bodies that one frame's physics system holds when the backend creates it, ghosts and character bodies
    /// included. When a full system needs one more body, the backend replaces it with a system of twice the limits (all
    /// three limits here) and moves every body over. That move costs time in proportion to the frame's bodies, once
    /// each time the frame doubles, and it writes a warning. A frame that is known to be large starts larger.
    /// </summary>
    public int MaxBodiesPerFrame { get; init; } = 4096;

    /// <summary>The most body pairs that one frame's broad phase can report per step, until the frame's system grows.</summary>
    public int MaxBodyPairsPerFrame { get; init; } = 8192;

    /// <summary>The most contact constraints that one frame's solver can hold per step, until the frame's system grows.</summary>
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

    /// <summary>The steepest slope that a character can stand on, in degrees.</summary>
    public float CharacterMaxSlopeDegrees { get; init; } = 50f;

    /// <summary>The largest force with which a character pushes dynamic bodies, in newtons.</summary>
    public float CharacterMaxStrength { get; init; } = 100f;

    /// <summary>
    /// When the proper acceleration of a frame has moved more than this, in m/s², from its value when the frame's bodies
    /// last woke, physics wakes every body in the frame, so that sleeping bodies react to a ship that starts or stops
    /// thrusting, however slowly its thrust changes. A change of the frame's gravity field wakes them too. It sits above
    /// the noise of a proper acceleration measured from float velocities at orbital speed.
    /// </summary>
    public float WakeAccelerationThreshold { get; init; } = 0.02f;

    /// <summary>
    /// The fastest rate, in radians per second, at which a walking character that touches nothing turns its up toward the
    /// up against its gravity (docs/plans/spin-gravity.md, "Characters"). A character that touches anything turns its up
    /// at once. The rate keeps a flip of the felt gravity for one step, such as the jolt of a hull or the start of an
    /// engine, from turning a floating person over, and it is fast enough that a person who jumps across a spinning
    /// station comes down feet first.
    /// </summary>
    public float CharacterAirUpTurnRate { get; init; } = 1f;

    /// <summary>Receives warnings. If it is null, warnings go to <see cref="System.Diagnostics.Trace"/>.</summary>
    public Action<string>? Log { get; init; }
}
