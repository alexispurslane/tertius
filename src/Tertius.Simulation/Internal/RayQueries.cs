using System.Numerics;

namespace Tertius.Simulation;

/// <summary>
/// Runs the ray casts that the entities asked for during a tick, at the sync point, and queues each answer as a
/// <see cref="RayHit"/> engine event for the routing that follows. It runs after the snapshot swap, so that Jolt (at the
/// end of the step, with this sync point's destroys and spawns applied) and the snapshot that the query reads (component
/// flags, collision groups, frames and poses) describe the same moment. Once warm, it allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// A ray cast in frame F walks the frames that it passes through. Each visit casts in one frame's physics system, and in
/// the systems of that frame's numeric frames, over a part of the ray, a range of distances from its origin:
/// </para>
/// <list type="bullet">
/// <item>F gets the whole ray.</item>
/// <item>A child frame gets the part of the ray that lies in its content: its bubble, grown to hold every body of its
/// members (see <see cref="ContentOf"/>). Its own interior and contents exist only in its own system, which holds a moving
/// body until it is past the hand-off margin outside the bubble, and a static body wherever it is.</item>
/// <item>The parent of a frame that the ray started in, or came up from, gets the rest of the range after the ray leaves
/// that frame's bubble, so its cast never starts inside that frame's hull. The parent does not go back down into it.</item>
/// </list>
/// <para>
/// A visit maps the ray from F into its frame through the frame tree, and maps each hit's normal back. The point of a hit is
/// the origin plus the direction times the distance, in F, in doubles. One entity can be hit in two systems, for example
/// through its ghost, or as a hull and an interior, so the hits keep the nearest one of each entity. They are sorted by
/// distance, then by entity index. A numeric frame never appears in a request or an answer.
/// </para>
/// </remarks>
internal sealed class RayQueries
{
    /// <summary>One frame whose system a ray is cast in, over the distances from <see cref="Start"/> to <see cref="End"/>.</summary>
    private readonly record struct Visit(FrameHandle Frame, double Start, double End, FrameHandle CameFrom, bool GoesUp);

    /// <summary>
    /// A semantic frame and one frame under it: a child frame, or one of its numeric frames, nested ones included. Built
    /// once per sync point that has requests, because the tree does not change while the requests run.
    /// </summary>
    private readonly record struct FrameLink(FrameHandle Parent, FrameHandle Child, bool Numeric);

    /// <summary>A hit in the frame of the ray: the normal is in that frame's axes.</summary>
    private readonly record struct Hit(Entity Entity, double Distance, Vector3 Normal);

    private readonly World world;
    private readonly EntityTable entities;
    private readonly FrameTree frames;
    private readonly IPhysicsBackend? physics;
    private readonly Router router;
    private readonly Action<string> reportError;
    private readonly AskerFilter filter;
    private readonly List<RayCastHit> systemHits = new(16);
    private readonly RefList<Hit> hits = new(16);
    private readonly RefList<Visit> visits = new(8);
    private readonly RefList<FrameLink> links = new(16);

    /// <summary>The content scale of each frame, by frame slot, and the sync point that worked it out: see <see cref="ContentOf"/>.</summary>
    private (int Stamp, double Scale)[] contentScales = new (int, double)[16];

    /// <summary>Counts the sync points that ran requests, so that a content scale of an earlier one is never read as current.</summary>
    private int syncStamp;

    private static readonly Comparison<Hit> ByDistanceThenEntity = static (a, b) =>
        a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Entity.CompareTo(b.Entity);

    public RayQueries(World world, IPhysicsBackend? physics, Router router, Action<string> reportError)
    {
        this.world = world;
        entities = world.Entities;
        frames = world.Frames;
        this.physics = physics;
        this.router = router;
        this.reportError = reportError;
        filter = new AskerFilter(world);
    }

    /// <summary>Runs the requests of every chunk, in chunk order and so in entity order, and queues their answers.</summary>
    public void Run(ReadOnlySpan<ChunkState> chunks, long tick)
    {
        bool linked = false;
        foreach (ChunkState chunk in chunks)
        {
            foreach (ref readonly RayRequest request in chunk.RayRequests.AsSpan())
            {
                // An entity that died at this sync point would get no answer: routing drops events to stale handles.
                if (!entities.IsAlive(request.Asker))
                    continue;
                if (!linked)
                {
                    LinkFrames();
                    syncStamp++;
                    linked = true;
                }
                try
                {
                    Answer(request);
                }
                // A query's Where predicate is the component's code, and its exception is the component's error, as in a run.
                // A broken contract is a bug, so it is not survived like one.
                catch (Exception ex) when (ex is not ContractException)
                {
                    reportError($"tick {tick}: {request.Asker}: a ray cast failed and has no answer: {ex.GetType().Name}: {ex.Message}");
                }
                if (filter.DeadHits > 0)
                    reportError($"tick {tick}: {request.Asker}: physics reported {filter.DeadHits} bodies of entities that are not live, which only a bug in the backend produces. The ray skipped them.");
            }
        }
    }

    private void LinkFrames()
    {
        links.Clear();
        int count = frames.Count;
        for (int i = 0; i < count; i++)
        {
            FrameHandle frame = frames.AtDense(i);
            if (frames.IsNumeric(frame))
            {
                links.Add(new FrameLink(frames.SemanticOf(frame), frame, Numeric: true));
                continue;
            }
            FrameHandle parent = frames.Parent(frame);
            if (!parent.IsNull)
                links.Add(new FrameLink(parent, frame, Numeric: false));
        }
    }

    private void Answer(in RayRequest request)
    {
        hits.Clear();
        filter.Begin(request.Asker, request.Query);
        bool frameAlive = frames.IsAlive(request.Frame);
        if (physics != null && frameAlive)
            Walk(request);
        Emit(request, frameAlive);
    }

    private void Walk(in RayRequest request)
    {
        bool nearest = request.Kind == RayCastKind.Nearest;
        visits.Clear();
        visits.Add(new Visit(request.Frame, 0, request.Length, FrameHandle.Null, GoesUp: true));
        while (visits.Count > 0)
        {
            Visit visit = visits[visits.Count - 1];
            visits.Truncate(visits.Count - 1);
            // The nearest hit so far ends every later part of the ray that would only find farther ones.
            double end = Math.Min(visit.End, Reach(request, nearest));
            if (visit.Start > end)
                continue;

            DPose toVisit = frames.Relative(request.Frame, visit.Frame);
            Double3 origin = toVisit.TransformPoint(request.From), direction = toVisit.Rotation.Rotate(request.Direction);
            Cast(visit.Frame, origin, direction, visit.Start, end, toVisit.Rotation, request.Kind);

            double reach = Reach(request, nearest);
            foreach (ref readonly FrameLink link in links.AsSpan())
            {
                if (link.Parent != visit.Frame || link.Child == visit.CameFrom)
                    continue;
                DPose toChild = frames.Relative(request.Frame, link.Child);
                Double3 childOrigin = toChild.TransformPoint(request.From), childDirection = toChild.Rotation.Rotate(request.Direction);
                if (link.Numeric)
                {
                    // A numeric frame has no bubble: its bodies are the frame's own, wherever they are.
                    Cast(link.Child, childOrigin, childDirection, visit.Start, end, toChild.Rotation, request.Kind);
                    continue;
                }
                // A child's system can hold a body anywhere in its content, and the ray meets it there wherever the ray
                // came from, so only the ray itself bounds the part.
                if (!ContentOf(link.Child).TryIntersectLine(childOrigin, childDirection, out double entry, out double exit))
                    continue;
                double start = Math.Max(0, entry), stop = Math.Min(reach, exit);
                if (start < stop)
                    visits.Add(new Visit(link.Child, start, stop, visit.Frame, GoesUp: false));
            }

            FrameHandle parent = visit.GoesUp ? frames.Parent(visit.Frame) : FrameHandle.Null;
            if (!parent.IsNull)
            {
                // The parent's part starts where the ray leaves this frame's bubble, or at once if it starts outside it.
                double start = visit.Start;
                if (frames.Bounds(visit.Frame).TryIntersectLine(origin, direction, out double entry, out double exit) && entry <= start && start < exit)
                    start = exit;
                if (start < end)
                    visits.Add(new Visit(parent, start, end, visit.Frame, GoesUp: true));
            }
        }
        hits.AsSpan().Sort(ByDistanceThenEntity);
    }

    /// <summary>The farthest distance along the ray at which a hit can still count: the nearest hit so far, or the end of the ray.</summary>
    private double Reach(in RayRequest request, bool nearest) =>
        nearest && hits.Count > 0 ? Math.Min(request.Length, hits[0].Distance) : request.Length;

    /// <summary>
    /// The content of <paramref name="frame"/>: its bubble, grown until it holds the bounding sphere of every body of the
    /// frame's members, which the frame's system or one of its numeric frames holds. A moving body leaves a frame only past
    /// the hand-off margin, and a static body never leaves it, so a body can lie anywhere outside the bubble. Each frame's
    /// content is worked out once per sync point, from the snapshot, which matches Jolt after the swap.
    /// </summary>
    /// <remarks>
    /// The content is the bubble scaled by a factor k of at least 1. A body with its origin at p and a bounding radius r fits
    /// in the bubble scaled by |p / radii| + r / (smallest radius): p lies on the bubble scaled by |p / radii|, and scaling an
    /// ellipsoid by one more unit of s / (smallest radius) moves its surface out by at least s in every direction.
    /// </remarks>
    private FrameBounds ContentOf(FrameHandle frame)
    {
        FrameBounds bubble = frames.Bounds(frame);
        if (frame.Index >= contentScales.Length)
            Array.Resize(ref contentScales, Math.Max(frame.Index + 1, 2 * contentScales.Length));
        ref (int Stamp, double Scale) known = ref contentScales[frame.Index];
        if (known.Stamp != syncStamp)
        {
            var radii = (Double3)bubble.Radii;
            double smallest = bubble.MinRadius, scale = 1;
            foreach (Entity member in world.FrameMembers(frame))
                if (entities.Body(member) is not null)
                    scale = Math.Max(scale, (entities.FrontRow(member).Pose.Position / radii).Length + entities.BodyBoundingRadius(member) / smallest);
            known = (syncStamp, scale);
        }
        return new FrameBounds(bubble.Radii * (float)known.Scale);
    }

    /// <summary>
    /// Casts in the system of <paramref name="frame"/>, where the ray has <paramref name="origin"/> and
    /// <paramref name="direction"/>, and keeps the nearest hit of each entity. <paramref name="toFrame"/> turns the
    /// ray's axes into the frame's.
    /// </summary>
    private void Cast(FrameHandle frame, Double3 origin, Double3 direction, double start, double end, DQuaternion toFrame, RayCastKind kind)
    {
        systemHits.Clear();
        physics!.CastRay(frame, origin, direction, start, end, filter, kind, systemHits);
        DQuaternion back = toFrame.Conjugate();
        foreach (RayCastHit found in systemHits)
            Keep(new Hit(found.Entity, found.Distance, back.Rotate(found.Normal)));
        if (kind == RayCastKind.Nearest && hits.Count > 1)
            hits.AsSpan().Sort(ByDistanceThenEntity);
    }

    /// <summary>Adds <paramref name="hit"/>, or replaces the hit of the same entity when this one is nearer.</summary>
    private void Keep(in Hit hit)
    {
        foreach (ref Hit kept in hits.AsSpan())
        {
            if (kept.Entity != hit.Entity)
                continue;
            if (hit.Distance < kept.Distance)
                kept = hit;
            return;
        }
        hits.Add(hit);
    }

    /// <summary>
    /// Queues the answers to <paramref name="request"/>: for the nearest hit one answer, for every hit one answer each,
    /// and a miss when there is no hit. Points and normals go into the frame that the asker is in now.
    /// </summary>
    private void Emit(in RayRequest request, bool frameAlive)
    {
        FrameHandle askerFrame = entities.FrontRow(request.Asker).Frame;
        bool convert = frameAlive && !askerFrame.IsNull && askerFrame != request.Frame;
        DPose toAsker = convert ? frames.Relative(request.Frame, askerFrame) : DPose.Identity;
        int count = request.Kind == RayCastKind.Nearest ? Math.Min(1, hits.Count) : hits.Count;
        for (int i = 0; i < count; i++)
        {
            ref Hit hit = ref hits[i];
            Double3 point = request.From + request.Direction * hit.Distance;
            Send(request.Asker, new RayHit
            {
                Entity = hit.Entity,
                Point = convert ? toAsker.TransformPoint(point) : point,
                Distance = (float)hit.Distance,
                Tag = request.Tag,
                Normal = convert ? toAsker.Rotation.Rotate(hit.Normal) : hit.Normal,
            });
        }
        if (count == 0)
        {
            Double3 end = request.From + request.Direction * request.Length;
            Send(request.Asker, new RayHit
            {
                Point = !frameAlive ? default : convert ? toAsker.TransformPoint(end) : end,
                Distance = (float)request.Length,
                Tag = request.Tag,
            });
        }
    }

    private void Send(Entity asker, in RayHit answer) => router.AddEngineEvent(Event.Create(new Target(asker), Entity.Null, answer));

    /// <summary>
    /// The filter of the request that runs: the request's entity query on the snapshot, around the asker, which it never
    /// accepts. The backend calls it inside Jolt, for each candidate body.
    /// </summary>
    private sealed class AskerFilter(World world) : IPhysicsQueryFilter
    {
        private Snapshot snapshot;
        private EntityQuery query;
        private Entity asker;

        /// <summary>How many candidates of the request named an entity that is not live, which only a bug produces.</summary>
        public int DeadHits { get; private set; }

        public void Begin(Entity askingEntity, in EntityQuery entityQuery)
        {
            asker = askingEntity;
            query = entityQuery;
            snapshot = new Snapshot(world, askingEntity);
            DeadHits = 0;
        }

        public bool Accepts(Entity entity)
        {
            if (entity == asker)
                return false;
            if (!world.Entities.IsAlive(entity))
            {
                // After the swap, Jolt holds bodies only for live entities.
                Contract.Assert(false, $"Physics reported a body of {entity}, which is not live.");
                DeadHits++;
                return false;
            }
            return query.Matches(snapshot, entity, default);
        }
    }
}
