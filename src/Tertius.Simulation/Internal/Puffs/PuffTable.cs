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
    private readonly EntityTable entities;
    private readonly FrameTree frames;
    private readonly Action<string> reportError;
    private readonly int capacity;
    // The share of the next puff that each emitter's entity has gathered, from 0 up to but not including 1. The entity
    // table clears it when it releases the entity, so a new entity in a reused slot starts at 0. Only AddNewborns writes it.
    private readonly EntityTable.Column<double> phases;
    private readonly PuffLooks looks = new();

    // The live rows, packed from 0, and the array that a rebuild writes them into in the new order before the two swap.
    private Puff[] rows = [];
    private Puff[] spare = [];
    private int count;
    // One range per frame that holds puffs, in the order of the rows. A rebuild writes the new ranges into the other list and
    // the two swap.
    private List<PuffRange> ranges = new();
    private List<PuffRange> rebuilt = new();

    // The newborns of step 7c, from their births to the rebuild at the end of the same step, and one run of them per
    // emitter that made some, in slot order.
    private Puff[] newborns = [];
    private int newbornCount;
    private readonly List<PuffRange> newbornRuns = new();

    // Scratch of the rebuild: the ranges and the runs that it has copied, and the ages for the cap's selection.
    private bool[] rangeCopied = [];
    private bool[] runCopied = [];
    private float[] ages = [];

    // The length of the step that the rows describe, in s, and the tick of that step.
    private double step;
    private long lastStepTick = long.MinValue;

    // Which rows the running rebuild leaves out for the cap: every row older than the edge age, and the first rows at it.
    private bool dropAll;
    private float edgeAge = float.PositiveInfinity;
    private int dropsAtEdge;

    /// <param name="entities">The entity table, which keeps the emitter bits and the birth phases.</param>
    /// <param name="frames">The frame tree, which gives each frame's loose-body step.</param>
    /// <param name="capacity">The most puffs that live at once, 0 or more.</param>
    /// <param name="reportError">Records a state that only a bug makes, in <see cref="World.Errors"/>.</param>
    public PuffTable(EntityTable entities, FrameTree frames, int capacity, Action<string> reportError)
    {
        Contract.Requires(capacity >= 0);
        this.entities = entities;
        this.frames = frames;
        this.capacity = capacity;
        this.reportError = reportError;
        phases = entities.AddColumn<double>();
    }

    /// <summary>The most puffs that live at once, <see cref="WorldOptions.PuffCapacity"/>. 0 turns puffs off.</summary>
    public int Capacity => capacity;

    /// <summary>The number of live puffs.</summary>
    public int Count => count;

    /// <summary>The look table.</summary>
    public PuffLooks Looks => looks;

    /// <summary>
    /// The live rows, their ranges and the look table, for the render snapshot. Valid until the table next changes, at the
    /// next sync point.
    /// </summary>
    public PuffView View => new(rows.AsSpan(0, count), CollectionsMarshal.AsSpan(ranges), looks.All, step);

    /// <summary>The birth phase of an emitter's entity, in [0, 1). Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    public double BirthPhase(Entity entity) => phases[entity];

    // ---- Step 2b ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Step 2b of the sync point of tick <paramref name="tick"/>: every puff takes the end of the step that just ended, by the
    /// rule of motion of its frame, and the puffs whose age reached their life die. It reads the loose-body step of each
    /// range's frame once, which describes the step that ended from step 2 of the sync point on. It removes rows only, so it
    /// compacts the survivors toward row 0 in place. A range whose frame is not live comes only from a bug: a Debug build
    /// fails a contract, and a Release build drops the range and records an error.
    /// </summary>
    /// <param name="tick">The tick of the sync point, later than the tick of the last step.</param>
    /// <param name="dt">The length of the step, in s: the World's float step.</param>
    public void Step(long tick, float dt)
    {
        Contract.Requires(tick > lastStepTick);
        lastStepTick = tick;
        step = dt;
        if (count == 0)
            return;
        Span<Puff> all = rows.AsSpan(0, count);
        Span<PuffRange> list = CollectionsMarshal.AsSpan(ranges);
        int write = 0, kept = 0;
        foreach (PuffRange range in list)
        {
            if (!frames.IsAlive(range.Frame))
            {
                DropRange(range, tick);
                continue;
            }
            LooseBodyStep loose = frames.LastLooseBodyStep(range.Frame);
            int start = write;
            for (int i = range.Start, end = range.Start + range.Count; i < end; i++)
            {
                ref readonly PuffLook look = ref looks[all[i].Look];
                Puff next = all[i].Stepped(in loose, in look, dt);
                if (next.Age < look.Life)
                    all[write++] = next;
                else
                    looks.Release(next.Look);
            }
            if (write > start)
                list[kept++] = new PuffRange(range.Frame, start, write - start);
        }
        ranges.RemoveRange(kept, ranges.Count - kept);
        count = write;
        Contract.Ensures(HoldsRows());
    }

    /// <summary>Drops a range whose frame is not live, which only a bug makes, and records it.</summary>
    private void DropRange(PuffRange range, long tick)
    {
        string message = $"tick {tick}: {range.Count} puffs are in the frame {range.Frame}, which is not live. Only a bug in the engine " +
            "makes this state: the destroy path moves the puffs of every dying frame into its heir. The puffs were dropped.";
        Contract.Assert(false, message);
        foreach (Puff row in rows.AsSpan(range.Start, range.Count))
            looks.Release(row.Look);
        reportError(message);
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
        if (count == 0)
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
                    looks.Release(row.Look);
                list[r] = range with { Count = 0 };
                continue;
            }
            Contract.Requires(frames.IsAlive(heir) && frames.IsAncestorOf(heir, range.Frame));
            DPose relative = frames.Relative(range.Frame, heir);
            foreach (ref Puff row in dying)
            {
                row.Chord = frames.ConvertVelocity(range.Frame, heir, row.Place, row.Chord);
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
    /// transform row, which holds the end of the step from step 7 on. Of all the newborns of the step, only the last ones up
    /// to the capacity are made, but every phase gathers its whole rate. When the survivors and the newborns pass the
    /// capacity, the oldest survivors die first. The rebuild then puts each frame's newborns at the end of its range.
    /// </summary>
    /// <param name="tick">The tick of the sync point, whose <see cref="Step"/> ran before.</param>
    /// <param name="dt">The length of the step, in s: the World's float step.</param>
    public void AddNewborns(long tick, float dt)
    {
        Contract.Requires(lastStepTick == tick);
        if (capacity == 0)
            return;
        long total = 0;
        foreach (Entity emitter in entities.EmitterEntities)
            total += PuffBirths.Count(phases[emitter], entities.FrontRender(emitter).Emitter.Rate, dt, out _);
        if (total == 0)
            return;
        long skip = Math.Max(0, total - capacity);
        EnsureNewborns((int)Math.Min(total, capacity));

        long seen = 0;
        foreach (Entity entity in entities.EmitterEntities)
        {
            ref readonly PuffEmitter emitter = ref entities.FrontRender(entity).Emitter;
            ref double phase = ref phases[entity];
            double start = phase;
            long made = PuffBirths.Count(start, emitter.Rate, dt, out phase);
            // The newborns from the first one past the skipped ones on, numbered from 1 in the emitter's step.
            long first = Math.Max(1, skip - seen + 1);
            seen += made;
            if (first > made)
                continue;
            AddNewbornsOf(entity, in emitter, start, first, made, tick, dt);
        }

        int excess = count + newbornCount - capacity;
        Rebuild(drop: Math.Max(0, excess));
        newbornCount = 0;
        newbornRuns.Clear();
        Contract.Ensures(HoldsRows() && looks.IsCoherent(rows.AsSpan(0, count)) && newbornRuns.Count == 0);
    }

    /// <summary>The newborns <paramref name="first"/> to <paramref name="last"/> of one emitter's step, as one run.</summary>
    private void AddNewbornsOf(Entity entity, in PuffEmitter emitter, double phase, long first, long last, long tick, float dt)
    {
        ref readonly TransformRow row = ref entities.BackRow(entity);
        // The owner of a root frame has no row frame: its puffs are born in the frame that it owns, at its origin.
        FrameHandle frame = row.Frame.IsNull ? entities.OwnedFrame(entity) : row.Frame;
        DPose pose = FrameReads.PoseIn(frames, entity, row, frame);
        Double3 velocity = FrameReads.VelocityIn(frames, entity, row, frame);
        LooseBodyStep loose = frames.LastLooseBodyStep(frame);
        int runStart = newbornCount;
        for (long j = first; j <= last; j++)
        {
            double age = PuffBirths.AgeAt(j, phase, emitter.Rate, dt);
            // A look that lives less than a step can make a newborn that would already be dead.
            if ((float)age >= emitter.Look.Life)
                continue;
            (double r1, double r2) = PuffBirths.Seed(entity, tick, j);
            Double3 direction = PuffBirths.Direction(emitter.Spread, r1, r2);
            newborns[newbornCount++] = PuffBirths.Newborn(in loose, in pose, velocity, in emitter, direction, age, look: -1, dt);
        }
        int born = newbornCount - runStart;
        if (born == 0)
            return;
        int look = looks.Use(emitter.Look, born);
        foreach (ref Puff newborn in newborns.AsSpan(runStart, born))
            newborn.Look = look;
        newbornRuns.Add(new PuffRange(frame, runStart, born));
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
        EnsureRows(count - drop + newbornCount);
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
            write = CopyNewborns(frame, runs, runDone, write);
            if (write > start)
                rebuilt.Add(new PuffRange(frame, start, write - start));
        }
        for (int u = 0; u < runs.Length; u++)
        {
            if (runDone[u])
                continue;
            int start = write;
            write = CopyNewborns(runs[u].Frame, runs, runDone, write);
            rebuilt.Add(new PuffRange(runs[u].Frame, start, write - start));
        }
        (rows, spare) = (spare, rows);
        (ranges, rebuilt) = (rebuilt, ranges);
        count = write;
    }

    /// <summary>Sets which survivors the running rebuild leaves out: the <paramref name="drop"/> oldest, and among equal ages the first in the rows.</summary>
    private void ChooseDrops(int drop)
    {
        dropAll = drop >= count && drop > 0;
        edgeAge = float.PositiveInfinity;
        dropsAtEdge = 0;
        if (drop == 0 || dropAll)
            return;
        // Every step adds the same float step to every age, so the order of the ages is the order of the births.
        Span<float> sorted = Scratch(ref ages, count);
        for (int i = 0; i < count; i++)
            sorted[i] = rows[i].Age;
        sorted.Sort();
        edgeAge = sorted[count - drop];
        for (int i = count - drop; i < count && sorted[i] == edgeAge; i++)
            dropsAtEdge++;
    }

    /// <summary>Copies the rows of <paramref name="range"/> that the cap keeps into the spare array from <paramref name="write"/>.</summary>
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
            looks.Release(row.Look);
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

    private int Grown(int length, int needed) => Math.Min(capacity, Math.Max(needed, Math.Max(16, length * 2)));

    /// <summary>A cleared span of <paramref name="length"/> entries of a scratch array, which grows by doubling.</summary>
    private static Span<T> Scratch<T>(ref T[] array, int length)
    {
        if (array.Length < length)
            array = new T[Math.Max(length, array.Length * 2)];
        Span<T> span = array.AsSpan(0, length);
        span.Clear();
        return span;
    }

    // ---- Invariants ---------------------------------------------------------------------------------------------------

    /// <summary>Checks every invariant of the class comment. Outside a tick, or at the end of a sync point.</summary>
    public void AssertInvariants()
    {
        Contract.Invariant(HoldsRows());
        looks.AssertInvariants(rows.AsSpan(0, count));
        Contract.Invariant(newbornCount == 0 && newbornRuns.Count == 0);
        foreach (double phase in phases.AsSpan())
            Contract.Invariant(phase >= 0 && phase < 1);
    }

    /// <summary>Invariants 1 to 4. It allocates nothing, so that the contracts of the sync point can call it.</summary>
    private bool HoldsRows()
    {
        if (count > capacity || !HoldsRanges())
            return false;
        foreach (Puff row in rows.AsSpan(0, count))
        {
            if (!IsFinite(row.Place) || !IsFinite(row.Chord) || !(row.Age >= 0) || looks.UsersOf(row.Look) == 0 || !(row.Age < looks[row.Look].Life))
                return false;
        }
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
            if (range.Start != next || range.Count <= 0 || !frames.IsAlive(range.Frame) || frames.IsNumeric(range.Frame))
                return false;
            for (int q = 0; q < r; q++)
                if (list[q].Frame == range.Frame)
                    return false;
            next += range.Count;
        }
        return next == count;
    }

    private static bool IsFinite(Double3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
}
