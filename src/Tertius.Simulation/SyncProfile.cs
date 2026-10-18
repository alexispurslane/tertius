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
        "4 collision intents, swap intents, samples MoveOn",
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

public sealed partial class World
{
    /// <summary>
    /// ter-sn28 profiling aid: runs one float view fill pass over every frame that is not numeric, outside a tick, so that a
    /// micro benchmark can compare them: 0 the pass of the sync point, 1 a pass frame by frame through MirrorFrameOwner, with a
    /// handle check per frame, as the pass before ter-sn28 walked.
    /// </summary>
    internal void ProfileMirrorPart(int part)
    {
        if (part == 0)
        {
            MirrorFrameOwners();
            return;
        }
        int n = Frames.Count;
        for (int i = 0; i < n; i++)
        {
            FrameHandle frame = Frames.AtDense(i);
            if (!Frames.IsNumeric(frame))
                MirrorFrameOwner(frame);
        }
    }
}

public sealed partial class World
{
    /// <summary>
    /// ter-sn28 profiling aid: a hash of the state that the sync point writes, to compare a prototype with the code before
    /// it bit for bit: the front and back transform rows of every live entity, in slot order, and the local pose and the
    /// velocities of every frame, in dense order.
    /// </summary>
    internal ulong ProfileStateHash()
    {
        ulong hash = 14695981039346656037;
        void Mix(ReadOnlySpan<byte> bytes)
        {
            foreach (byte b in bytes)
                hash = (hash ^ b) * 1099511628211;
        }
        var live = new List<Entity>();
        CopyLiveEntities(live);
        live.Sort((a, b) => a.Index.CompareTo(b.Index));
        foreach (Entity entity in live)
        {
            TransformRow front = entities.FrontRow(entity), back = entities.BackRow(entity);
            Mix(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<TransformRow>(in front)));
            Mix(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<TransformRow>(in back)));
        }
        for (int i = 0; i < Frames.Count; i++)
        {
            FrameHandle frame = Frames.AtDense(i);
            DPose local = Frames.TreeLocalPose(frame);
            Double3 velocity = Frames.TreeLinearVelocity(frame);
            System.Numerics.Vector3 angular = Frames.AngularVelocity(frame);
            Mix(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<DPose>(in local)));
            Mix(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<Double3>(in velocity)));
            Mix(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<System.Numerics.Vector3>(in angular)));
        }
        return hash;
    }
}
