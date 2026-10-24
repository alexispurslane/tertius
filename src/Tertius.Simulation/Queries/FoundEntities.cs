using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>
/// The entities of the front snapshot that a query matches, in entity index order. <see cref="Snapshot.Find"/> binds a
/// stored <see cref="EntityQuery"/> to the snapshot, and <see cref="Snapshot.Entities"/> starts the shorthand chain,
/// whose words build the same query. An ending runs it: <c>foreach</c>, <see cref="Count"/>, <see cref="Any"/>,
/// <see cref="First"/> or <see cref="CopyTo"/>.
/// </summary>
/// <remarks>
/// The chain allocates nothing. Its first <c>In</c>, <c>Near</c> and <c>Where</c> go into the query's inline parts; it
/// keeps the later ones on the stack, at most <see cref="StackFilters.Capacity"/> of them. A run starts from the smallest
/// candidate set that an index gives: the entities with one of the <c>With</c> types, the members of a frame, or the
/// cells of the spatial grid around a <c>Near</c> center. Then it tests every filter on each candidate. Any number of
/// threads can run queries during a tick: they read only the front snapshot, which nothing changes during the tick.
/// </remarks>
public readonly ref struct FoundEntities
{
    private readonly Snapshot snapshot;
    private readonly EntityQuery query;
    private readonly StackFilters stack;

    internal FoundEntities(scoped in Snapshot snapshot, scoped in EntityQuery query, scoped in StackFilters stack)
    {
        this.snapshot = snapshot;
        this.query = query;
        this.stack = stack;
    }

    /// <inheritdoc cref="EntityQuery.With{T}"/>
    public FoundEntities With<T>()
        where T : IComponent => new(snapshot, query.With<T>(), stack);

    /// <inheritdoc cref="EntityQuery.Without{T}"/>
    public FoundEntities Without<T>()
        where T : IComponent => new(snapshot, query.Without<T>(), stack);

    /// <inheritdoc cref="EntityQuery.InAll"/>
    public FoundEntities InAll(CollisionGroup group) => new(snapshot, query.InAll(group), stack);

    /// <inheritdoc cref="EntityQuery.InNone"/>
    public FoundEntities InNone(CollisionGroup group) => new(snapshot, query.InNone(group), stack);

    /// <inheritdoc cref="EntityQuery.InAny"/>
    public FoundEntities InAny(CollisionGroup group) => new(snapshot, query.InAny(group), stack);

    /// <inheritdoc cref="EntityQuery.In"/>
    public FoundEntities In(FrameHandle frame) =>
        query.InFrame.IsNull
            ? new(snapshot, query.In(frame), stack)
            : new(snapshot, query, stack.With(QueryFilter.In(frame)));

    /// <inheritdoc cref="EntityQuery.Near(double)"/>
    public FoundEntities Near(double radius) => WithNear(QueryFilter.Near(radius));

    /// <inheritdoc cref="EntityQuery.Near(Entity, double)"/>
    public FoundEntities Near(Entity other, double radius) => WithNear(QueryFilter.Near(other, radius));

    /// <inheritdoc cref="EntityQuery.Near(FrameHandle, Double3, double)"/>
    public FoundEntities Near(FrameHandle frame, Double3 point, double radius) =>
        WithNear(QueryFilter.Near(frame, point, radius));

    /// <inheritdoc cref="EntityQuery.Where"/>
    public FoundEntities Where(EntityPredicate predicate) =>
        query.WherePredicate is null
            ? new(snapshot, query.Where(predicate), stack)
            : new(snapshot, query, stack.With(QueryFilter.Where(predicate)));

    // Each chain word puts its filter inline while the query's slot for it is free, so it never builds a heap part.
    private FoundEntities WithNear(scoped in QueryFilter filter) =>
        !query.NearFilter.HasValue
            ? new(snapshot, query.WithNear(filter), stack)
            : new(snapshot, query, stack.With(filter));

    /// <summary>Returns the enumerator that <c>foreach</c> uses. It picks the index when it starts.</summary>
    public Enumerator GetEnumerator() => new(snapshot, query, stack);

    /// <summary>The number of matching entities.</summary>
    public int Count()
    {
        Enumerator run = GetEnumerator();
        try
        {
            int count = 0;
            while (run.MoveNext())
                count++;
            return count;
        }
        finally
        {
            run.Dispose();
        }
    }

    /// <summary>True if an entity matches.</summary>
    public bool Any() => First().HasValue;

    /// <summary>The matching entity with the lowest index. Null if none matches.</summary>
    public Entity? First()
    {
        Enumerator run = GetEnumerator();
        try
        {
            return run.MoveNext() ? run.Current : null;
        }
        finally
        {
            run.Dispose();
        }
    }

    /// <summary>Adds the matching entities to the end of <paramref name="results"/>, in entity index order.</summary>
    public void CopyTo(List<Entity> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        Enumerator run = GetEnumerator();
        try
        {
            while (run.MoveNext())
                results.Add(run.Current);
        }
        finally
        {
            run.Dispose();
        }
    }

    /// <summary>
    /// Walks the candidates of the index that the run starts from, and yields those that pass every filter. A run that
    /// starts from the spatial grid sorts its candidates into a buffer of the thread; <see cref="Dispose"/>, which
    /// <c>foreach</c> calls, returns it.
    /// </summary>
    public ref struct Enumerator
    {
        private readonly Snapshot snapshot;

        private readonly EntityQuery query;

        // Not readonly: its span view needs a reference to it.
        private readonly StackFilters stack;
        private readonly bool scanAll;
        private EntityTable.LiveEntityEnumerator all;
        private readonly ReadOnlySpan<Entity> candidates;
        private Entity[]? buffer;
        private int index;

        internal Enumerator(scoped in Snapshot snapshot, scoped in EntityQuery query, scoped in StackFilters stack)
        {
            this.snapshot = snapshot;
            this.query = query;
            this.stack = stack;
            index = -1;
            Current = Entity.Null;
            QueryPlan plan = QueryPlanner.Choose(snapshot, query, stack.Items);
            Index = plan.Index;
            CandidateCount = plan.Count;
            switch (plan.Index)
            {
                case QueryIndex.All:
                    scanAll = true;
                    all = snapshot.World.Entities.LiveEntities;
                    break;
                case QueryIndex.Grid:
                    // The grid lists the entities of a cell in no order, so the run sorts them into entity index order.
                    buffer = QueryBuffers.Rent(plan.Count);
                    Span<Entity> near = buffer.AsSpan(
                        0,
                        snapshot.World.Spatial.CollectNear(
                            frame: plan.Frame,
                            center: plan.Point,
                            radius: plan.Radius,
                            into: buffer
                        )
                    );
                    near.Sort();
                    candidates = near;
                    break;
                case QueryIndex.Components or QueryIndex.Frame or QueryIndex.Nothing:
                    candidates = plan.Candidates;
                    break;
                default:
                    throw new UnreachableException($"A query plan has the index {plan.Index}, which is not a member.");
            }
        }

        /// <summary>The index that the run starts from.</summary>
        internal readonly QueryIndex Index { get; }

        /// <summary>How many candidates the index gives, before the filters: an upper bound of the matches.</summary>
        internal readonly int CandidateCount { get; }

        public Entity Current { get; private set; }

        public bool MoveNext()
        {
            ReadOnlySpan<QueryFilter> more = stack.Items;
            if (scanAll)
            {
                while (all.MoveNext())
                {
                    Entity entity = all.Current;
                    if (query.Matches(snapshot, entity, more))
                    {
                        Current = entity;
                        return true;
                    }
                }

                return false;
            }

            while (++index < candidates.Length)
            {
                Entity entity = candidates[index];
                if (query.Matches(snapshot, entity, more))
                {
                    Current = entity;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Returns the thread's buffer, if the run took one.</summary>
        public void Dispose()
        {
            if (buffer is { } taken)
            {
                buffer = null;
                QueryBuffers.Return(taken);
            }
        }
    }
}

/// <summary>
/// The filters of a shorthand chain past its query's inline parts, on the stack, in the order of the calls. The default
/// value holds none.
/// </summary>
internal struct StackFilters
{
    /// <summary>The most filters that a chain keeps on the stack.</summary>
    public const int Capacity = 8;

    private Items8 items;
    private int count;

    /// <summary>The filters, in the order of the calls.</summary>
    [UnscopedRef]
    public readonly ReadOnlySpan<QueryFilter> Items => ((ReadOnlySpan<QueryFilter>)items)[..count];

    [InlineArray(Capacity)]
    private struct Items8
    {
        private QueryFilter first;
    }

    /// <summary>A copy with <paramref name="filter"/> added. Throws <see cref="InvalidOperationException"/> past
    /// <see cref="Capacity"/>.</summary>
    public readonly StackFilters With(in QueryFilter filter)
    {
        if (count == Capacity)
            throw new InvalidOperationException(
                $"A chain from Snapshot.Entities keeps at most {Capacity} In, Near and Where filters beyond its first of each. Store the query as a static readonly EntityQuery instead."
            );
        StackFilters copy = this;
        copy.items[copy.count++] = filter;
        return copy;
    }
}
