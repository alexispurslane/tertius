using System.Diagnostics;

namespace Tertius.Simulation;

/// <summary>
/// The time that each step of the sync point took, summed over sync points, in <see cref="Stopwatch"/> timestamp units
/// (ter-sn28). The sync point calls <see cref="Start"/> once and <see cref="Lap"/> after each step, so each lap is the time
/// since the previous one. Both are <see cref="ConditionalAttribute"/> on the symbol SYNC_PROFILE, which a build defines only
/// with the MSBuild property SyncProfile=true (see Tertius.Simulation.csproj), so a normal build compiles every call away and
/// pays nothing. The benchmarks print the laps when they run on such a build:
/// <c>dotnet run -c Release -p:SyncProfile=true --project benchmarks/Tertius.Benchmarks -- --quick kepler1 10</c>.
/// </summary>
internal sealed class SyncProfile
{
    /// <summary>True when this build records the laps (SYNC_PROFILE is defined).</summary>
    internal static readonly bool Enabled =
#if SYNC_PROFILE
        true;
#else
        false;
#endif

    internal static readonly string[] Names =
    [
        "1 end gameplay, chunk messages, component types",
        "2a advance numeric frames",
        "2b apply physics output",
        "2c FollowOwners: records loop",
        "2c FollowOwners: orbit jumps",
        "2c FollowOwners: MirrorFrameOwners",
        "2d route step contacts (+ MoveMembership)",
        "3 debug invariants",
        "4 collision intents, motion changes, swap intents",
        "5 subscriptions, destroys",
        "6 spawns live",
        "6c numeric update, flush physics changes",
        "7a MirrorFrameOwners",
        "7b DeriveAttachments",
        "7c convert moved intents",
        "8a CopyDirtyRows",
        "8b Frames.Update",
        "8c spatial.Update",
        "9 physics queries",
        "10 route messages",
        "11 render snapshot",
        "12 EndTick",
        "  (part of 2b) output check and hand-off reparents",
        "  (part of 6c) gather numeric bodies",
        "  (part of 6c) FrameTree.UpdateNumericFrames",
        "2c FollowFramelessEntities",
        "2 (end) puffs: PuffTable.Step",
        "7 (end) puffs: PuffTable.AddNewborns",
    ];

    internal readonly long[] Totals = new long[Names.Length];
    private long last;
    // True from Start to the last step's lap, so that a call outside the sync point, such as a numeric update in
    // PrepareTick, records nothing.
    private bool active;

    [Conditional("SYNC_PROFILE")]
    internal void Start()
    {
        last = Stopwatch.GetTimestamp();
        active = true;
    }

    [Conditional("SYNC_PROFILE")]
    internal void Lap(int step)
    {
        if (!active)
            return;
        if (step == 21)
            active = false;
        long now = Stopwatch.GetTimestamp();
        Totals[step] += now - last;
        last = now;
    }
}
