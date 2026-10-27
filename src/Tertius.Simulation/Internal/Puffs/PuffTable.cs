using System.Runtime.InteropServices;

namespace Tertius.Simulation;

/// <summary>
/// The puffs of a World (docs/plans/particles.md, "The puff store"). Puffs are not entities: each is a row of this table,
/// with a place, a chord, an age and a look index. The rows of one frame lie together in one range, so no row holds a
/// frame, and what the puffs of an emitter share lies once in the look table. The World owns the table, and only its sync
/// point and its destroy path write it, each through one method that the World calls once:
/// <list type="bullet">
/// <item>Step 2b, <see cref="Step"/>: every puff takes the end of the step that just ended, by the rule of motion of its
/// frame (see <see cref="Puff.Stepped"/>), and the puffs whose age reached their life die.</item>
/// <item>The destroy path, <see cref="MoveOutOfDyingFrames"/>: the puffs of each dying frame move into its heir, before the
/// frame dies.</item>
/// <item>Step 7c, <see cref="AddNewborns"/>: the emitters of the step make their newborns, the cap drops the oldest puffs,
/// and the rebuild puts each frame's newborns at the end of its range.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// The order of the rows is the order of the steps, of the snapshot and of the draws, so it is the same in every run. Each
/// frame keeps the place of its first range in the list, and a frame that gets its first puffs takes a new range at the end,
/// in the order of its first newborn. Inside a range the survivors keep their order, the puffs that a frame's death moves in
/// follow the heir's own puffs when the heir's range came first and come before them otherwise, and the newborns come last,
/// in slot order and in each emitter's order of birth. A death removes rows, and the rest keep their order. A range that
/// empties leaves the list.
/// </para>
/// <para>
/// Invariants, checked by <see cref="AssertInvariants"/>, which the World's own check calls:
/// <list type="number">
/// <item>The count of live puffs, the sum of the ranges' counts, is at most the capacity.</item>
/// <item>Every range names a live semantic frame, and no two ranges name the same frame.</item>
/// <item>The ranges lie packed from row 0, in list order, and no range is empty.</item>
/// <item>Every row's place and chord are finite, its age is 0 or more and less than its look's life, and its look index
/// names an entry in use.</item>
/// <item>Each look's count equals the number of rows that name it. The dictionary of the looks maps each entry in use to
/// its index and holds nothing else, and the free stack holds exactly the other indices below the table's end.</item>
/// <item>Every look in use is valid.</item>
/// <item>No newborn waits outside step 7c.</item>
/// <item>Every birth phase is in [0, 1).</item>
/// </list>
/// The entity table keeps the emitter bit of each slot, and its own check holds that bit to the front render row.
/// </para>
/// </remarks>
internal sealed class PuffTable
{
    /// <summary>
    /// The default of <see cref="StepParallelThreshold"/>, in live puffs. Measured on ter-fwsy with PuffTableBenchmarks.Workers
    /// (Apple M1 Max, 1-minute load 7.6 to 9.7), in a steady table whose 64 oldest puffs die in each step, so the join moves
    /// every slice after the first. Medians of the step, sync thread against workers: 48.6 against 48.8 us at 2,048 puffs
    /// in a frame that does not turn and 68.1 against 58.7 us in a turning frame with a medium, 59.6 against 55.0 us and
    /// 102.8 against 72.5 us at 3,072. So 3,072 is the fewest puffs at which the medians favour the workers in both frames.
    /// </summary>
    internal const int DefaultStepParallelThreshold = 3072;

    /// <summary>
    /// How many slices a step on the workers cuts the rows into. More slices than workers lets a fast worker take the work of
    /// a slow one, and any count gives the same rows.
    /// </summary>
    private const int StepSlices = 16;

    private readonly EntityTable entities;
    private readonly FrameTree frames;
    private readonly Action<string> reportError;
    private readonly ParallelOptions? workers;

    // The share of the next puff that each emitter's entity has gathered, from 0 up to but not including 1. The entity
    // table clears it when it releases the entity, so a new entity in a reused slot starts at 0. Only AddNewborns writes it.
    private readonly EntityTable.Column<double> phases;

    // The live rows, packed from 0, and the array that a rebuild writes them into in the new order before the two swap.
    private Puff[] rows = [];
    private Puff[] spare = [];

    // One range per frame that holds puffs, in the order of the rows. A rebuild writes the new ranges into the other list and
    // the two swap.
    private List<PuffRange> ranges = [];
    private List<PuffRange> rebuilt = [];

    // The newborns of step 7c, from their births to the rebuild at the end of the same step, and one run of them per
    // emitter that made some, in slot order.
    private Puff[] newborns = [];
    private int newbornCount;
    private readonly List<PuffRange> newbornRuns = [];

    // Scratch of the rebuild: the ranges and the runs that it has copied, and the ages for the cap's selection.
    private bool[] rangeCopied = [];
    private bool[] runCopied = [];
    private float[] ages = [];

    // The length of the step that the rows describe, in s, and the tick of that step.
    private double step;
    private long lastStepTick = long.MinValue;

    // Scratch of the step of 2b: the loose-body step of each range's frame, null for a frame that is not live; and per slice
    // its survivors and its deaths, the look of each dead row from the slice's first row on, and its survivors in each range
    // that it overlaps, at the range's index plus the slice's. A slice's overlaps lie after those of the slice before,
    // because slice k + 1 starts in the range where slice k ends or in a later one.
    private LooseBodyStep?[] rangeSteps = [];
    private int[] sliceKept = [];
    private int[] sliceDeaths = [];
    private int[] deadLooks = [];
    private int[] overlapKept = [];
    private int[] rangeKept = [];
    private int sliceCount;
    private readonly Action<int> stepSlice;

    // Which rows the running rebuild leaves out for the cap: every row older than the edge age, and the first rows at it.
    private bool dropAll;
    private float edgeAge = float.PositiveInfinity;
    private int dropsAtEdge;

    /// <param name="entities">The entity table, which keeps the emitter bits and the birth phases.</param>
    /// <param name="frames">The frame tree, which gives each frame's loose-body step.</param>
    /// <param name="capacity">The most puffs that live at once, 0 or more.</param>
    /// <param name="reportError">Records a state that only a bug makes, in <see cref="World.Errors"/>.</param>
    /// <param name="workers">The workers on which a large step runs, or null to run every step on the calling thread.</param>
    public PuffTable(
        EntityTable entities,
        FrameTree frames,
        int capacity,
        Action<string> reportError,
        ParallelOptions? workers = null
    )
    {
        Contract.Requires(capacity >= 0);
        this.entities = entities;
        this.frames = frames;
        Capacity = capacity;
        this.reportError = reportError;
        this.workers = workers;
        phases = entities.AddColumn<double>();
        stepSlice = StepSlice;
    }

    /// <summary>The most puffs that live at once, <see cref="WorldOptions.PuffCapacity"/>. 0 turns puffs off.</summary>
    public int Capacity { get; }

    /// <summary>
    /// The fewest live puffs for which a step runs on the workers. 1 or more. Both paths give the same rows, so only tests
    /// change it: at 1, every step with a puff takes the workers.
    /// </summary>
    internal int StepParallelThreshold
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = DefaultStepParallelThreshold;

    /// <summary>The number of live puffs.</summary>
    public int Count { get; private set; }

    /// <summary>The look table.</summary>
    public PuffLooks Looks { get; } = new();

    /// <summary>
    /// The live rows, their ranges and the look table, for the render snapshot. Valid until the table next changes, at the
    /// next sync point.
    /// </summary>
    public PuffView View =>
        new(rows: rows.AsSpan(0, Count), ranges: CollectionsMarshal.AsSpan(ranges), looks: Looks.All, step: step);

    /// <summary>The birth phase of an emitter's entity, in [0, 1). Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    public double BirthPhase(Entity entity) => phases[entity];

    // ---- Step 2b ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Step 2b of the sync point of tick <paramref name="tick"/>: every puff takes the end of the step that just ended, by the
    /// rule of motion of its frame, and the puffs whose age reached their life die. It reads the loose-body step of each
    /// range's frame once, which describes the step that ended from step 2 of the sync point on. It cuts the rows into equal
    /// slices, on the workers from <see cref="StepParallelThreshold"/> puffs on, and each slice compacts its survivors toward
    /// its first row. The join then moves the slices together in slice order (docs/plans/particles.md, "The parallel
    /// pieces"). A range whose frame is not live comes only from a bug: a Debug build fails a contract, and a Release build
    /// drops the range and records an error.
    /// </summary>
    /// <param name="tick">The tick of the sync point, later than the tick of the last step.</param>
    /// <param name="dt">The length of the step, in s: the World's float step.</param>
    public void Step(long tick, float dt)
    {
        Contract.Requires(tick > lastStepTick);
        lastStepTick = tick;
        step = dt;
        if (Count == 0)
            return;
        ReadRangeSteps(tick);
        Grow(ref deadLooks, Count);
        if (workers is { } options && Count >= StepParallelThreshold)
        {
            SetSlices(Math.Min(StepSlices, Count));
            Parallel.For(fromInclusive: 0, toExclusive: sliceCount, parallelOptions: options, body: stepSlice);
        }
        else
        {
            SetSlices(1);
            StepSlice(0);
        }

        JoinSlices();
        Contract.Ensures(HoldsRows());
    }

    /// <summary>The loose-body step of each range's frame, or null for a frame that is not live, which it reports.</summary>
    private void ReadRangeSteps(long tick)
    {
        ReadOnlySpan<PuffRange> list = CollectionsMarshal.AsSpan(ranges);
        Grow(ref rangeSteps, list.Length);
        for (int r = 0; r < list.Length; r++)
        {
            bool alive = frames.IsAlive(list[r].Frame);
            rangeSteps[r] = alive ? frames.LastLooseBodyStep(list[r].Frame) : null;
            if (!alive)
                ReportDeadRange(list[r], tick);
        }
    }

    /// <summary>Records a range whose frame is not live, which only a bug makes. Its slices drop its puffs.</summary>
    private void ReportDeadRange(PuffRange range, long tick)
    {
        string message =
            $"tick {tick}: {range.Count} puffs are in the frame {range.Frame}, which is not live. Only a bug in the engine "
            + "makes this state: the destroy path moves the puffs of every dying frame into its heir. The puffs were dropped.";
        Contract.Assert(false, message);
        reportError(message);
    }

    /// <summary>Cuts the rows into <paramref name="count"/> slices for the step that runs.</summary>
    private void SetSlices(int count)
    {
        sliceCount = count;
        Grow(ref sliceKept, count);
        Grow(ref sliceDeaths, count);
        Grow(ref overlapKept, ranges.Count + count);
    }

    /// <summary>The first row of slice <paramref name="slice"/>, and of the slice after the last one the row count.</summary>
    private int SliceStart(int slice) => (int)((long)Count * slice / sliceCount);

    /// <summary>The index of the range that holds <paramref name="row"/>, a live row, by a search by halves of the starts.</summary>
    private static int RangeOf(ReadOnlySpan<PuffRange> list, int row)
    {
        int low = 0,
            high = list.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (list[middle].Start <= row)
                low = middle;
            else
                high = middle - 1;
        }

        return low;
    }

    /// <summary>
    /// Steps the rows of one slice. It writes only its own rows, compacted toward its first row, its own stretch of the dead
    /// looks, its own counts, and its survivors in each range that it overlaps, at the range's index plus the slice's.
    /// </summary>
    private void StepSlice(int slice)
    {
        int start = SliceStart(slice),
            end = SliceStart(slice + 1);
        Span<Puff> all = rows.AsSpan(0, Count);
        ReadOnlySpan<PuffRange> list = CollectionsMarshal.AsSpan(ranges);
        // The float step that Step widened into the double step, back as it came: the narrowing of a widened float is exact.
        float dt = (float)step;
        int write = start,
            dead = start;
        for (int r = RangeOf(list, start); r < list.Length && list[r].Start < end; r++)
        {
            int from = Math.Max(start, list[r].Start),
                to = Math.Min(end, list[r].Start + list[r].Count),
                kept = write;
            if (rangeSteps[r] is { } loose)
            {
                for (int i = from; i < to; i++)
                {
                    ref readonly PuffLook look = ref Looks[all[i].Look];
                    Puff next = all[i].Stepped(in loose, in look, dt);
                    if (next.Age < look.Life)
                        all[write++] = next;
                    else
                        deadLooks[dead++] = next.Look;
                }
            }
            else
            {
                for (int i = from; i < to; i++)
                    deadLooks[dead++] = all[i].Look;
            }

            overlapKept[r + slice] = write - kept;
        }

        sliceKept[slice] = write - start;
        sliceDeaths[slice] = dead - start;
    }

    /// <summary>
    /// Joins the slices in slice order: moves each one's survivors after those of the slices before, frees the looks of its
    /// dead rows, and adds its survivors to the ranges. A range left empty leaves the list, and the others keep their order.
    /// </summary>
    private void JoinSlices()
    {
        Span<PuffRange> list = CollectionsMarshal.AsSpan(ranges);
        Span<int> kept = Scratch(ref rangeKept, list.Length);
        int write = 0;
        for (int slice = 0; slice < sliceCount; slice++)
        {
            int start = SliceStart(slice),
                end = SliceStart(slice + 1);
            // Slice order is row order, so the survivors keep their order and the looks are freed in the order of one thread.
            if (write != start)
                rows.AsSpan(start, sliceKept[slice]).CopyTo(rows.AsSpan(write));
            write += sliceKept[slice];
            foreach (int look in deadLooks.AsSpan(start, sliceDeaths[slice]))
                Looks.Release(look);
            for (int r = RangeOf(list, start); r < list.Length && list[r].Start < end; r++)
                kept[r] += overlapKept[r + slice];
        }

        int live = 0,
            next = 0;
        for (int r = 0; r < list.Length; r++)
        {
            if (kept[r] == 0)
                continue;
            list[live++] = new(list[r].Frame, next, kept[r]);
            next += kept[r];
        }

        ranges.RemoveRange(live, ranges.Count - live);
        Count = write;
    }

    // ---- The destroy path ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The destroy path, before the dying frames die: the puffs of each dying frame move into its heir, the nearest
    /// surviving ancestor, or die with it when it has none. A place converts by the pose of the dying frame in the heir, and
    /// a chord as a velocity, with the frame's velocity and its ω × r term, as <c>World.Rehome</c> converts an entity's row.
    /// The rebuild then merges the ranges of each heir into one. Both describe the end of the last step, because step 2b
    /// has stepped the puffs and step 2 the frames.
    /// </summary>
    /// <param name="heirOf">
    /// For each frame that holds puffs: the frame itself when it survives, its nearest surviving ancestor when it dies, or
    /// null when it dies and no ancestor survives.
    /// </param>
    public void MoveOutOfDyingFrames(Func<FrameHandle, FrameHandle> heirOf)
    {
        if (Count == 0)
            return;
        bool moved = false;
        Span<PuffRange> list = CollectionsMarshal.AsSpan(ranges);
        for (int r = 0; r < list.Length; r++)
        {
            PuffRange range = list[r];
            FrameHandle heir = heirOf(range.Frame);
            if (heir == range.Frame)
                continue;
            moved = true;
            Span<Puff> dying = rows.AsSpan(range.Start, range.Count);
            if (heir.IsNull)
            {
                foreach (Puff row in dying)
                    Looks.Release(row.Look);
                list[r] = range with { Count = 0 };
                continue;
            }

            Contract.Requires(frames.IsAlive(heir) && frames.IsAncestorOf(heir, range.Frame));
            DPose relative = frames.Relative(range.Frame, heir);
            foreach (ref Puff row in dying)
            {
                row.Chord = frames.ConvertVelocity(from: range.Frame, to: heir, position: row.Place, v: row.Chord);
                row.Place = relative.Position + relative.Rotation.RotateNormalizing(row.Place);
            }

            list[r] = range with { Frame = heir };
        }

        if (moved)
            Rebuild(drop: 0);
        Contract.Ensures(HoldsRanges());
    }

    // ---- Step 7c ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Step 7c of the sync point of tick <paramref name="tick"/>: every emitter of the step that just ended makes its newborns,
    /// in slot order. An emitter of the step is one whose entity was in the step and is still live: its emitter bit is set
    /// (see <see cref="EntityTable.EmitterEntities"/>). Its place and velocity at the end of the step come from its back
    /// transform row, which holds the end of the step from step 7 on. Of all the newborns of the step that live at its end,
    /// only the last ones up to the capacity are made, but every phase gathers its whole rate. When the survivors and the newborns pass the
    /// capacity, the oldest survivors die first. The rebuild then puts each frame's newborns at the end of its range.
    /// </summary>
    /// <param name="tick">The tick of the sync point, whose <see cref="Step"/> ran before.</param>
    /// <param name="dt">The length of the step, in s: the World's float step.</param>
    public void AddNewborns(long tick, float dt)
    {
        Contract.Requires(lastStepTick == tick);
        if (Capacity == 0)
            return;
        // Only the newborns that live count toward the capacity, so the skip never removes one that would live in favor of
        // one that is already dead.
        long total = 0;
        foreach (Entity entity in entities.EmitterEntities)
            total += Living(
                emitter: in entities.FrontRender(entity).Emitter,
                phase: phases[entity],
                dt: dt,
                first: out _,
                next: out _
            );
        long skip = Math.Max(0, total - Capacity);
        EnsureNewborns((int)Math.Min(total, Capacity));

        long seen = 0;
        foreach (Entity entity in entities.EmitterEntities)
        {
            ref readonly PuffEmitter emitter = ref entities.FrontRender(entity).Emitter;
            ref double phase = ref phases[entity];
            double start = phase;
            long living = Living(
                emitter: in emitter,
                phase: start,
                dt: dt,
                first: out long firstLiving,
                next: out phase
            );
            // The newborns from the first living one past the skipped ones on, numbered from 1 in the emitter's step.
            long first = firstLiving + Math.Max(0, skip - seen);
            long last = firstLiving + living - 1;
            seen += living;
            if (first > last)
                continue;
            AddNewbornsOf(
                entity: entity,
                emitter: in emitter,
                phase: start,
                first: first,
                last: last,
                tick: tick,
                dt: dt
            );
        }

        // A step without newborns changes no row: the count was within the capacity already.
        if (newbornCount == 0)
            return;

        int excess = Count + newbornCount - Capacity;
        Rebuild(drop: Math.Max(0, excess));
        newbornCount = 0;
        newbornRuns.Clear();
        Contract.Ensures(HoldsRows() && Looks.IsCoherent(rows.AsSpan(0, Count)) && newbornRuns.Count == 0);
    }

    /// <summary>
    /// How many of the newborns of one emitter's step live at the end of the step, and <paramref name="first"/>, the index
    /// of the first of them (see <see cref="PuffBirths.FirstLiving"/>). <paramref name="next"/> is the birth phase where the
    /// next step starts.
    /// </summary>
    private static long Living(in PuffEmitter emitter, double phase, float dt, out long first, out double next)
    {
        long made = PuffBirths.Count(phase: phase, rate: emitter.Rate, dt: dt, next: out next);
        first = PuffBirths.FirstLiving(count: made, phase: phase, rate: emitter.Rate, dt: dt, life: emitter.Look.Life);
        return made - first + 1;
    }

    /// <summary>The newborns <paramref name="first"/> to <paramref name="last"/> of one emitter's step, all of which live, as one run.</summary>
    private void AddNewbornsOf(
        Entity entity,
        in PuffEmitter emitter,
        double phase,
        long first,
        long last,
        long tick,
        float dt
    )
    {
        ref readonly TransformRow row = ref entities.BackRow(entity);
        // The owner of a root frame has no row frame: its puffs are born in the frame that it owns, at its origin.
        FrameHandle frame = row.Frame.IsNull ? entities.OwnedFrame(entity) : row.Frame;
        DPose pose = FrameReads.PoseIn(frames: frames, entity: entity, row: row, to: frame);
        Double3 velocity = FrameReads.VelocityIn(frames: frames, entity: entity, row: row, to: frame);
        LooseBodyStep loose = frames.LastLooseBodyStep(frame);
        int runStart = newbornCount;
        for (long j = first; j <= last; j++)
        {
            double age = PuffBirths.AgeAt(index: j, phase: phase, rate: emitter.Rate, dt: dt);
            (double r1, double r2) = PuffBirths.Seed(entity, tick, j);
            Double3 direction = PuffBirths.Direction(emitter.Spread, r1, r2);
            newborns[newbornCount++] = PuffBirths.Newborn(
                step: in loose,
                emitterPose: in pose,
                emitterVelocity: velocity,
                emitter: in emitter,
                direction: direction,
                age: age,
                look: -1,
                dt: dt
            );
        }

        int born = newbornCount - runStart;
        int look = Looks.Use(emitter.Look, born);
        foreach (ref Puff newborn in newborns.AsSpan(runStart, born))
            newborn.Look = look;
        newbornRuns.Add(new(frame, runStart, born));
    }

    // ---- The rebuild --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes the rows into the spare array in the new order and swaps the two: for each frame in the order of its first
    /// range in the list, each of the frame's ranges in list order, less the rows that the cap drops, then the frame's
    /// newborns; then the newborns of the frames that held no range, in the order of their first newborn. Each frame ends
    /// with one range. It frees the looks of the dropped rows.
    /// </summary>
    /// <param name="drop">How many of the oldest survivors the cap drops. With newborns that fill the capacity, all of them.</param>
    private void Rebuild(int drop)
    {
        ChooseDrops(drop);
        EnsureRows(Count - drop + newbornCount);
        Span<PuffRange> list = CollectionsMarshal.AsSpan(ranges);
        Span<PuffRange> runs = CollectionsMarshal.AsSpan(newbornRuns);
        Span<bool> rangeDone = Scratch(ref rangeCopied, list.Length);
        Span<bool> runDone = Scratch(ref runCopied, runs.Length);
        rebuilt.Clear();
        int write = 0;
        for (int r = 0; r < list.Length; r++)
        {
            if (rangeDone[r])
                continue;
            FrameHandle frame = list[r].Frame;
            int start = write;
            for (int q = r; q < list.Length; q++)
            {
                if (rangeDone[q] || list[q].Frame != frame)
                    continue;
                rangeDone[q] = true;
                write = CopySurvivors(list[q], write);
            }

            write = CopyNewborns(frame: frame, runs: runs, runDone: runDone, write: write);
            if (write > start)
                rebuilt.Add(new(frame, start, write - start));
        }

        for (int u = 0; u < runs.Length; u++)
        {
            if (runDone[u])
                continue;
            int start = write;
            write = CopyNewborns(frame: runs[u].Frame, runs: runs, runDone: runDone, write: write);
            rebuilt.Add(new(runs[u].Frame, start, write - start));
        }

        (rows, spare) = (spare, rows);
        (ranges, rebuilt) = (rebuilt, ranges);
        Count = write;
    }

    /// <summary>Sets which survivors the running rebuild leaves out: the <paramref name="drop"/> oldest, and among equal ages the first in the rows.</summary>
    private void ChooseDrops(int drop)
    {
        dropAll = drop >= Count && drop > 0;
        edgeAge = float.PositiveInfinity;
        dropsAtEdge = 0;
        if (drop == 0 || dropAll)
            return;
        // Every step adds the same float step to every age, so the order of the ages is the order of the births.
        Span<float> sorted = Scratch(ref ages, Count);
        for (int i = 0; i < Count; i++)
            sorted[i] = rows[i].Age;
        sorted.Sort();
        edgeAge = sorted[Count - drop];
        for (int i = Count - drop; i < Count && sorted[i] == edgeAge; i++)
            dropsAtEdge++;
    }

    /// <summary>Copies the rows of <paramref name="range"/> that the cap keeps into the spare array from
    /// <paramref name="write"/>.</summary>
    private int CopySurvivors(PuffRange range, int write)
    {
        Span<Puff> from = rows.AsSpan(range.Start, range.Count);
        if (!dropAll && edgeAge == float.PositiveInfinity)
        {
            from.CopyTo(spare.AsSpan(write));
            return write + from.Length;
        }

        foreach (Puff row in from)
        {
            bool dropped = dropAll || row.Age > edgeAge || (row.Age == edgeAge && dropsAtEdge > 0);
            if (!dropped)
            {
                spare[write++] = row;
                continue;
            }

            if (!dropAll && row.Age == edgeAge)
                dropsAtEdge--;
            Looks.Release(row.Look);
        }

        return write;
    }

    /// <summary>Copies the newborns of every run of <paramref name="frame"/> that is not copied yet into the spare array.</summary>
    private int CopyNewborns(FrameHandle frame, ReadOnlySpan<PuffRange> runs, Span<bool> runDone, int write)
    {
        for (int u = 0; u < runs.Length; u++)
        {
            if (runDone[u] || runs[u].Frame != frame)
                continue;
            runDone[u] = true;
            newborns.AsSpan(runs[u].Start, runs[u].Count).CopyTo(spare.AsSpan(write));
            write += runs[u].Count;
        }

        return write;
    }

    // ---- Storage ------------------------------------------------------------------------------------------------------

    /// <summary>Grows the rows and the spare array to hold <paramref name="needed"/> rows, by doubling, up to the capacity.</summary>
    private void EnsureRows(int needed)
    {
        if (spare.Length >= needed && rows.Length >= needed)
            return;
        int size = Grown(rows.Length, needed);
        Array.Resize(ref rows, size);
        spare = new Puff[size];
    }

    /// <summary>Grows the newborns' array to hold <paramref name="needed"/> newborns, by doubling, up to the capacity.</summary>
    private void EnsureNewborns(int needed)
    {
        if (newborns.Length < needed)
            newborns = new Puff[Grown(newborns.Length, needed)];
    }

    private int Grown(int length, int needed) => Math.Min(Capacity, Math.Max(needed, Math.Max(16, length * 2)));

    /// <summary>A cleared span of <paramref name="length"/> entries of a scratch array, which grows by doubling.</summary>
    private static Span<T> Scratch<T>(ref T[] array, int length)
    {
        if (array.Length < length)
            array = new T[Math.Max(length, array.Length * 2)];
        Span<T> span = array.AsSpan(0, length);
        span.Clear();
        return span;
    }

    /// <summary>Grows a scratch array to hold <paramref name="length"/> entries, by doubling, and keeps what it holds.</summary>
    private static void Grow<T>(ref T[] array, int length)
    {
        if (array.Length < length)
            array = new T[Math.Max(length, array.Length * 2)];
    }

    // ---- Invariants ---------------------------------------------------------------------------------------------------

    /// <summary>Checks every invariant of the class comment. Outside a tick, or at the end of a sync point.</summary>
    public void AssertInvariants()
    {
        Contract.Invariant(HoldsRows());
        Looks.AssertInvariants(rows.AsSpan(0, Count));
        Contract.Invariant(newbornCount == 0 && newbornRuns.Count == 0);
        foreach (double phase in phases.AsSpan())
            Contract.Invariant(phase is >= 0 and < 1);
    }

    /// <summary>Invariants 1 to 4. It allocates nothing, so that the contracts of the sync point can call it.</summary>
    private bool HoldsRows()
    {
        if (Count > Capacity || !HoldsRanges())
            return false;
        foreach (Puff row in rows.AsSpan(0, Count))
            if (
                !IsFinite(row.Place)
                || !IsFinite(row.Chord)
                || !(row.Age >= 0)
                || Looks.UsersOf(row.Look) == 0
                || !(row.Age < Looks[row.Look].Life)
            )
                return false;
        return true;
    }

    /// <summary>Invariants 2 and 3, and that the ranges cover exactly the live rows.</summary>
    private bool HoldsRanges()
    {
        ReadOnlySpan<PuffRange> list = CollectionsMarshal.AsSpan(ranges);
        int next = 0;
        for (int r = 0; r < list.Length; r++)
        {
            PuffRange range = list[r];
            if (
                range.Start != next
                || range.Count <= 0
                || !frames.IsAlive(range.Frame)
                || frames.IsNumeric(range.Frame)
            )
                return false;
            for (int q = 0; q < r; q++)
                if (list[q].Frame == range.Frame)
                    return false;
            next += range.Count;
        }

        return next == Count;
    }

    private static bool IsFinite(Double3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
}
