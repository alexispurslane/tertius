using System.Diagnostics;
using System.Numerics;

namespace Tertius.Simulation;

/// <summary>The index that a run of an entity query starts from.</summary>
internal enum QueryIndex : byte
{
    /// <summary>Every live entity: no filter of the query has an index.</summary>
    All,

    /// <summary>The entities that have one of the query's <c>With</c> types.</summary>
    Components,

    /// <summary>The members of a frame, from an <c>In</c> or from the frame of a <c>Near</c> center.</summary>
    Frame,

    /// <summary>The cells of the spatial grid that a <c>Near</c> sphere touches.</summary>
    Grid,

    /// <summary>No entity: a <c>Near</c> center that is not in a frame, so the filter matches nothing.</summary>
    Nothing,
}

/// <summary>
/// The candidates that a run starts from. For <see cref="QueryIndex.Components"/> and <see cref="QueryIndex.Frame"/>,
/// <see cref="Candidates"/> holds them in entity index order. For <see cref="QueryIndex.Grid"/>, the run collects the
/// entities of the grid in the frame <see cref="Frame"/> within <see cref="Radius"/> of <see cref="Point"/>.
/// </summary>
internal readonly ref struct QueryPlan
{
    public QueryIndex Index { get; init; }

    /// <summary>How many candidates the index gives: an upper bound of the matches.</summary>
    public int Count { get; init; }

    public ReadOnlySpan<Entity> Candidates { get; init; }

    public FrameHandle Frame { get; init; }

    public Double3 Point { get; init; }

    public double Radius { get; init; }
}

/// <summary>
/// Picks the index that a run of an entity query starts from: of the indexes that the query's filters can use, the one
/// with the fewest candidates. <c>Without</c>, the collision mask and <c>Where</c> have no index: no index lists what an
/// entity lacks, and a predicate can test anything. Worker-safe during a tick.
/// </summary>
internal static class QueryPlanner
{
    /// <summary>
    /// The plan of a run of <paramref name="query"/> with the further filters <paramref name="more"/>. Throws
    /// <see cref="InvalidOperationException"/> for a <c>Near(radius)</c> on a snapshot that has no asker.
    /// </summary>
    public static QueryPlan Choose(
        scoped in Snapshot snapshot,
        scoped in EntityQuery query,
        scoped ReadOnlySpan<QueryFilter> more
    )
    {
        EntityTable entities = snapshot.World.Entities;
        var best = new QueryPlan { Index = QueryIndex.All, Count = entities.LiveCount };
        foreach (int id in query.Components.With)
        {
            ReadOnlySpan<Entity> with = entities.WithComponent(id);
            if (with.Length < best.Count)
                best = new()
                {
                    Index = QueryIndex.Components,
                    Count = with.Length,
                    Candidates = with,
                };
        }

        if (!query.InFrame.IsNull)
            ConsiderFrame(ref best, snapshot.World, query.InFrame);
        Consider(ref best, snapshot, query.NearFilter);
        if (query.Extras != null)
            foreach (ref readonly QueryFilter filter in query.Extras.Filters)
                Consider(ref best, snapshot, filter);
        foreach (ref readonly QueryFilter filter in more)
            Consider(ref best, snapshot, filter);
        return best;
    }

    private static void Consider(scoped ref QueryPlan best, in Snapshot snapshot, in QueryFilter filter)
    {
        switch (filter)
        {
            case InFilter(var frame):
                ConsiderFrame(ref best, snapshot.World, frame);
                break;
            case NearAsker when snapshot.Asker.IsNull:
                throw new InvalidOperationException(
                    "Near(radius) is around the entity that runs the query, and this snapshot has none. Run it from a component's Self.Snapshot, or use Near(other, radius) or Near(frame, point, radius)."
                );
            case NearAsker(var radius):
                ConsiderNear(best: ref best, snapshot: snapshot, near: filter, radius: radius);
                break;
            case NearEntity { Radius: var radius }:
                ConsiderNear(best: ref best, snapshot: snapshot, near: filter, radius: radius);
                break;
            case NearPoint { Radius: var radius }:
                ConsiderNear(best: ref best, snapshot: snapshot, near: filter, radius: radius);
                break;
            case WhereFilter or null:
                // Where and the empty filter have no index.
                break;
            default:
                throw new UnreachableException($"The query filter {filter} has a case that the planner does not know.");
        }
    }

    /// <summary>The Near filter <paramref name="near"/>, whose radius is <paramref name="radius"/>, as an index: the frame of its center and the grid around it.</summary>
    private static void ConsiderNear(
        scoped ref QueryPlan best,
        in Snapshot snapshot,
        in QueryFilter near,
        double radius
    )
    {
        if (!near.TryCenter(snapshot, out FrameHandle frame, out Double3 point))
        {
            best = new() { Index = QueryIndex.Nothing, Count = 0 };
            return;
        }

        // Everything near the center is in the center's frame, so the frame's members are an index of the filter too.
        ConsiderFrame(ref best, snapshot.World, frame);
        int inCells = snapshot.World.Spatial.CountNear(frame, point, radius);
        if (inCells >= 0 && inCells < best.Count)
            best = new()
            {
                Index = QueryIndex.Grid,
                Count = inCells,
                Frame = frame,
                Point = point,
                Radius = radius,
            };
    }

    private static void ConsiderFrame(scoped ref QueryPlan best, World world, FrameHandle frame)
    {
        ReadOnlySpan<Entity> members = world.FrameMembers(frame);
        if (members.Length < best.Count)
            best = new()
            {
                Index = QueryIndex.Frame,
                Count = members.Length,
                Candidates = members,
            };
    }
}

/// <summary>
/// Buffers of entity handles for the runs of entity queries, one pool per thread, so that a run that sorts its
/// candidates allocates nothing once the pool is warm. Nested runs on one thread each take their own buffer.
/// </summary>
internal static class QueryBuffers
{
    [ThreadStatic]
    private static Stack<Entity[]>? free;

    /// <summary>A buffer that holds at least <paramref name="size"/> handles.</summary>
    public static Entity[] Rent(int size)
    {
        Stack<Entity[]> pool = free ??= new();
        if (pool.TryPop(out Entity[]? buffer) && buffer.Length >= size)
            return buffer;
        // A buffer that is too small is dropped, and the pool keeps the larger one when it comes back.
        return new Entity[Math.Max(64, (int)Math.Min(Array.MaxLength, BitOperations.RoundUpToPowerOf2((uint)size)))];
    }

    public static void Return(Entity[] buffer) => (free ??= new()).Push(buffer);
}
