#pragma warning disable CA2211, CA1707, CA1805, CA1002, CA1819
using System.Diagnostics;

namespace Tertius;

/// <summary>
/// SCRATCH (t88-hjjm): wall-time spans of one profiled film run, summed in <see cref="Stopwatch"/> units. The simulation,
/// the physics backend and the script runner add their spans here, and the script runner reads the sums at each film's
/// start and end. The ticking thread writes the main spans and the physics thread writes the physics spans; the script
/// runner reads them only between ticks, after the tick has waited for the physics thread. Not for merge.
/// </summary>
public static class ScratchProfile
{
    /// <summary>True when the environment variable TERTIUS_SCRATCH_PROFILE is set.</summary>
    public static readonly bool On = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TERTIUS_SCRATCH_PROFILE"));

    public const int Tick = 0,
        Prepare = 1,
        Gameplay = 2,
        PhysicsWait = 3,
        Sync = 4,
        FlushLog = 5,
        PhysicsStep = 6,
        PhysicsFrames = 7,
        PhysicsBefore = 8,
        PhysicsIntents = 9,
        PhysicsCharacters = 10,
        PhysicsSystems = 11,
        PhysicsAfter = 12,
        PhysicsContacts = 13,
        PhysicsHandOffs = 14,
        PhysicsGhosts = 15,
        PhysicsWrite = 16,
        Capture = 17,
        SinkWrite = 18,
        Readouts = 19,
        RunnerTicks = 20,
        PhysicsGate = 21;

    public static readonly string[] Names =
    [
        "tick (World.Tick)",
        "  prepare",
        "  gameplay",
        "  wait for physics after gameplay",
        "  sync point",
        "  flush log",
        "physics step (physics thread)",
        "  reconcile and refresh frames",
        "  hulls, forms, spawn and static hulls",
        "  intents and gravity",
        "  update characters",
        "  update systems (Jolt)",
        "  snap kinematic, proper accelerations",
        "  collect contacts",
        "  hand off bodies",
        "  ghosts and ended contacts",
        "  write bodies and frames",
        "capture (playback.Capture)",
        "  video sink write",
        "readout row",
        "runner ticks (RunTicks, incl. WaitForTick)",
        "  physics gate wait",
    ];

    public static readonly long[] Totals = new long[Names.Length];
    public static readonly long[] Counts = new long[Names.Length];

    /// <summary>Jolt systems that updated, their active rigid bodies and walking characters, summed over updates.</summary>
    public static long SystemsUpdated,
        SystemsSkipped,
        ActiveRigidBodies,
        WalkingCharacters,
        CharactersUpdated;

    /// <summary>The slowest Jolt system update of the current section: its frame's name and its time.</summary>
    public static long SlowestSystemUpdate;

    public static string SlowestSystemFrame = "";

    public static long Now() => Stopwatch.GetTimestamp();

    /// <summary>Adds the time since <paramref name="start"/> to span <paramref name="span"/> and returns the time now.</summary>
    public static long Lap(int span, long start)
    {
        long now = Stopwatch.GetTimestamp();
        Totals[span] += now - start;
        Counts[span]++;
        return now;
    }
}
