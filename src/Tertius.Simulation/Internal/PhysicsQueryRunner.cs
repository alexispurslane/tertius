using System.Diagnostics;
using System.Numerics;

namespace Tertius.Simulation;

/// <summary>
/// Runs the physics queries that the entities asked for during a tick, at the sync point: ray casts, shape casts and
/// overlap queries. It queues each answer as a <see cref="RayHit"/>, <see cref="ShapeHit"/> or <see cref="OverlapHit"/>
/// engine event for the routing that follows. It runs after the snapshot swap, so that Jolt (at the end of the step, with
/// this sync point's destroys and spawns applied) and the snapshot that the query reads (component flags, collision
/// groups, frames and poses) describe the same moment. Once warm, it allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// A query in frame F walks the frames whose bodies it can meet. A cast is a line from its start along its direction, and
/// an overlap is the single point of its shape's center, at distance 0. A shape reaches its bounding radius r from that
/// line or point, and a ray reaches 0. Each visit queries one frame's physics system, and the systems of that frame's
/// numeric frames, over a part of the line, a range of distances from its start:
/// </para>
/// <list type="bullet">
/// <item>F gets the whole line.</item>
/// <item>A child frame gets the part of the line within r of its content: its bubble, grown to hold every body of its
/// members (see <see cref="ContentScale"/>). Its own interior and contents exist only in its own system, which holds a
/// moving body until it is past the hand-off margin outside the bubble, and a static body wherever it is. Only the line
/// itself bounds the part, so a child of a parent that the query came up to gets its bodies in the bubble the query left
/// too.</item>
/// <item>The parent of a frame that the query started in, or came up from, gets the rest of the range from where the query
/// first reaches out of that frame's bubble: a ray where it leaves the bubble, a shape where it first sticks out. That
/// frame's hull is in the parent's system, and the query is inside it, so the parent's query skips that hull: the query
/// meets the frame's interior instead, in the frame's own system. The parent does not go back down into the frame.</item>
/// </list>
/// <para>
/// A visit maps the query from F into its frame through the frame tree, and maps each hit's point and normal back. The
/// point of a ray's hit is the origin plus the direction times the distance, in F, in doubles. One entity can be hit in two
/// systems, for example through its ghost, or as a hull and an interior, so the hits keep the nearest one of each entity.
/// They are sorted by distance, then by entity index, and an overlap's, at distance 0, by entity index. A numeric frame
/// never appears in a request or an answer.
/// </para>
/// </remarks>
internal sealed class PhysicsQueryRunner(
    World world,
    IPhysicsBackend? physics,
    Router router,
    Action<string> reportError
)
{
    private static readonly Comparison<Hit> ByDistanceThenEntity = static (a, b) =>
        a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Entity.CompareTo(b.Entity);

    private readonly World world = world;
    private readonly EntityTable entities = world.Entities;
    private readonly FrameTree frames = world.Frames;
    private readonly IPhysicsBackend? physics = physics;
    private readonly Router router = router;
    private readonly Action<string> reportError = reportError;
    private readonly AskerFilter filter = new(world);
    private readonly List<RayCastHit> rayHits = [with(16)];
    private readonly List<ShapeCastHit> shapeHits = [with(4)];
    private readonly List<Entity> overlapped = [with(16)];
    private readonly RefList<Hit> hits = new(16);
    private readonly RefList<Visit> visits = new(8);
    private readonly RefList<FrameLink> links = new(16);

    /// <summary>The content scale of each frame, by frame slot, and the sync point that worked it out: see
    /// <see cref="ContentScale"/>.</summary>
    private (int Stamp, double Scale)[] contentScales = new (int Stamp, double Scale)[16];

    /// <summary>Counts the sync points that ran requests, so that a content scale of an earlier one is never read as current.</summary>
    private int syncStamp;

    /// <summary>
    /// One frame whose system a query runs in, over the distances from <see cref="Start"/> to <see cref="End"/>.
    /// <see cref="CameFrom"/> is the frame the walk came from, and <see cref="GoesUp"/> is true for the frame the query
    /// started in and for each parent that the walk went up to.
    /// </summary>
    private readonly record struct Visit(
        FrameHandle Frame,
        double Start,
        double End,
        FrameHandle CameFrom,
        bool GoesUp
    );

    /// <summary>
    /// A semantic frame and one frame under it: a child frame, or one of its numeric frames, nested ones included. Built
    /// once per sync point that has requests, because the tree does not change while the requests run.
    /// </summary>
    private readonly record struct FrameLink(FrameHandle Parent, FrameHandle Child, bool Numeric);

    /// <summary>A hit in the frame of the query: the point is in that frame's coordinates, and the normal in its axes.</summary>
    private readonly record struct Hit(Entity Entity, double Distance, Double3 Point, Vector3 Normal);

    /// <summary>Runs the requests of every chunk, in chunk order and so in entity order, and queues their answers.</summary>
    public void Run(ReadOnlySpan<ChunkState> chunks, long tick)
    {
        bool linked = false;
        foreach (ChunkState chunk in chunks)
        foreach (ref readonly PhysicsRequest request in chunk.PhysicsRequests.AsSpan())
        {
            // An entity that died at this sync point would get no answer: routing drops events to stale handles.
            if (!entities.IsAlive(request.Asker))
                continue;
            // The World builds every request from a checked case, so only a bug leaves the question empty.
            if (!request.Question.HasValue)
            {
                Contract.Assert(false, $"The physics request of {request.Asker} holds no question.");
                reportError(
                    $"tick {tick}: {request.Asker}: a physics request holds no question, which only a bug produces. It has no answer."
                );
                continue;
            }

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
                reportError(
                    $"tick {tick}: {request.Asker}: the {NameOf(request.Question)} failed and has no answer: {ex.GetType().Name}: {ex.Message}"
                );
            }

            if (filter.DeadHits > 0)
                reportError(
                    $"tick {tick}: {request.Asker}: physics reported {filter.DeadHits} bodies of entities that are not live, which only a bug in the backend produces. The {NameOf(request.Question)} skipped them."
                );
        }
    }

    // Run refuses the empty question before it asks for a name, so the null arm only keeps the switch exhaustive.
    private static string NameOf(in PhysicsQuestion question) =>
        question switch
        {
            RayQuestion => "ray cast",
            ShapeCastQuestion => "shape cast",
            OverlapQuestion => "overlap query",
            null => "physics query",
        };

    private void LinkFrames()
    {
        links.Clear();
        int count = frames.Count;
        for (int i = 0; i < count; i++)
        {
            FrameHandle frame = frames.AtDense(i);
            if (frames.IsNumeric(frame))
            {
                links.Add(new(frames.SemanticOf(frame), frame, Numeric: true));
                continue;
            }

            FrameHandle parent = frames.Parent(frame);
            if (!parent.IsNull)
                links.Add(new(parent, frame, Numeric: false));
        }
    }

    private void Answer(in PhysicsRequest request)
    {
        hits.Clear();
        filter.Begin(request.Asker, request.Query);
        bool frameAlive = frames.IsAlive(request.Frame);
        if (physics is { } backend && frameAlive)
            Walk(request, backend);
        Emit(request, frameAlive);
    }

    private void Walk(in PhysicsRequest request, IPhysicsBackend backend)
    {
        PhysicsQuestion question = request.Question;
        bool firstOnly = question.FirstHitOnly;
        double reach = question.Reach,
            length = question.Length;
        // An overlap is a point, so any line through it tests whether it lies in a region. Run refuses the empty question
        // before it walks, so the null arm only keeps the switch exhaustive.
        Double3 line = question switch
        {
            RayQuestion ray => ray.Direction,
            ShapeCastQuestion shapeCast => shapeCast.Direction,
            OverlapQuestion => Double3.UnitX,
            null => Double3.Zero,
        };
        Double3 from = question.Origin;
        visits.Clear();
        visits.Add(new(Frame: request.Frame, Start: 0, End: length, CameFrom: FrameHandle.Null, GoesUp: true));
        while (visits.Count > 0)
        {
            Visit visit = visits[^1];
            visits.Truncate(visits.Count - 1);
            // The first hit so far ends every later part of the query that would only find farther ones.
            double end = Math.Min(visit.End, Limit(length, firstOnly));
            if (visit.Start > end)
                continue;

            // The parent's system holds the hull of the frame that the query came up from, around the query's start.
            filter.Skip(visit.GoesUp ? visit.CameFrom.Entity : Entity.Null);
            DPose toVisit = frames.Relative(request.Frame, visit.Frame);
            Query(
                backend: backend,
                frame: visit.Frame,
                request: request,
                toFrame: toVisit,
                start: visit.Start,
                end: end
            );

            double limit = Limit(length, firstOnly);
            foreach (ref readonly FrameLink link in links.AsSpan())
            {
                if (link.Parent != visit.Frame || link.Child == visit.CameFrom)
                    continue;
                DPose toChild = frames.Relative(request.Frame, link.Child);
                if (link.Numeric)
                {
                    // A numeric frame has no bubble: its bodies are the frame's own, wherever they are.
                    Query(
                        backend: backend,
                        frame: link.Child,
                        request: request,
                        toFrame: toChild,
                        start: visit.Start,
                        end: end
                    );
                    continue;
                }

                FrameBounds bubble = frames.Bounds(link.Child);
                var region = new FrameBounds(
                    bubble.Radii * (float)(ContentScale(link.Child) + reach / bubble.MinRadius)
                );
                if (
                    !region.TryIntersectLine(
                        origin: toChild.TransformPoint(from),
                        direction: toChild.Rotation.Rotate(line),
                        entry: out double entry,
                        exit: out double exit
                    )
                )
                    continue;
                double start = Math.Max(0, entry),
                    stop = Math.Min(limit, exit);
                if (start <= stop)
                    visits.Add(new(Frame: link.Child, Start: start, End: stop, CameFrom: visit.Frame, GoesUp: false));
            }

            FrameHandle parent = visit.GoesUp ? frames.Parent(visit.Frame) : FrameHandle.Null;
            if (!parent.IsNull)
            {
                // The parent's part starts where the query first reaches out of this frame's bubble, or at once if it
                // starts so: that is where it leaves the bubble shrunk by its reach.
                double start = visit.Start;
                FrameBounds bubble = frames.Bounds(visit.Frame);
                double inner = 1 - reach / bubble.MinRadius;
                if (
                    inner > 0
                    && new FrameBounds(bubble.Radii * (float)inner).TryIntersectLine(
                        origin: toVisit.TransformPoint(from),
                        direction: toVisit.Rotation.Rotate(line),
                        entry: out double entry,
                        exit: out double exit
                    )
                    && entry <= start
                    && start < exit
                )
                    start = exit;
                if (start <= end)
                    visits.Add(new(Frame: parent, Start: start, End: end, CameFrom: visit.Frame, GoesUp: true));
            }
        }

        filter.Skip(Entity.Null);
        // The hits of a ray or a shape cast are in order along the line. An overlap's hits have no order.
        bool alongLine = question switch
        {
            RayQuestion or ShapeCastQuestion => true,
            OverlapQuestion => false,
            null => false,
        };
        if (alongLine)
            hits.AsSpan().Sort(ByDistanceThenEntity);
        else
            KeepEachEntityOnce();
    }

    /// <summary>The farthest distance at which a hit can still count: the first hit so far, or the end of the query,
    /// <paramref name="length"/> meters from its start.</summary>
    private double Limit(double length, bool firstOnly) =>
        firstOnly && hits.Count > 0 ? Math.Min(length, hits[0].Distance) : length;

    /// <summary>
    /// The factor by which the bubble of <paramref name="frame"/> grows into its content, at least 1: the bubble times this
    /// factor holds the bounding sphere of every body of the frame's members, which the frame's system or one of its
    /// numeric frames holds. A moving body leaves a frame only past the hand-off margin, and a static body never leaves it,
    /// so a body can lie anywhere outside the bubble. Each frame's scale is worked out once per sync point, from the
    /// snapshot, which matches Jolt after the swap.
    /// </summary>
    /// <remarks>
    /// A body with its origin at p and a bounding radius r fits in the bubble scaled by |p / radii| + r / (smallest radius):
    /// p lies on the bubble scaled by |p / radii|, and scaling an ellipsoid by s / (smallest radius) more moves its surface
    /// out by at least s in every direction. By the same rule, the region of a query that reaches r is the content scaled
    /// by r / (smallest radius) more, and a shape of radius r lies wholly in the bubble while its center is in the bubble
    /// scaled by 1 - r / (smallest radius).
    /// </remarks>
    private double ContentScale(FrameHandle frame)
    {
        if (frame.Index >= contentScales.Length)
            Array.Resize(ref contentScales, Math.Max(frame.Index + 1, 2 * contentScales.Length));
        ref (int Stamp, double Scale) known = ref contentScales[frame.Index];
        if (known.Stamp != syncStamp)
        {
            FrameBounds bubble = frames.Bounds(frame);
            var radii = (Double3)bubble.Radii;
            double smallest = bubble.MinRadius,
                scale = 1;
            foreach (Entity member in world.FrameMembers(frame))
                if (entities.Body(member) is not null)
                    scale = Math.Max(
                        scale,
                        (entities.FrontRow(member).Pose.Position / radii).Length
                            + entities.BodyBoundingRadius(member) / smallest
                    );
            known = (syncStamp, scale);
        }

        return known.Scale;
    }

    /// <summary>
    /// Runs the query in the system of <paramref name="frame"/>, over the distances from <paramref name="start"/> to
    /// <paramref name="end"/>, and keeps the nearest hit of each entity. <paramref name="toFrame"/> maps the query's frame
    /// into <paramref name="frame"/>.
    /// </summary>
    private void Query(
        IPhysicsBackend backend,
        FrameHandle frame,
        in PhysicsRequest request,
        in DPose toFrame,
        double start,
        double end
    )
    {
        DQuaternion back = toFrame.Rotation.Conjugate();
        switch (request.Question)
        {
            case RayQuestion { Origin: var origin, Direction: var direction, Kind: var kind }:
                rayHits.Clear();
                backend.CastRay(
                    frame: frame,
                    origin: toFrame.TransformPoint(origin),
                    direction: toFrame.Rotation.Rotate(direction),
                    start: start,
                    end: end,
                    filter: filter,
                    kind: kind,
                    hits: rayHits
                );
                foreach (RayCastHit found in rayHits)
                    Keep(
                        new(
                            Entity: found.Entity,
                            Distance: found.Distance,
                            Point: origin + direction * found.Distance,
                            Normal: back.Rotate(found.Normal)
                        )
                    );
                break;
            case ShapeCastQuestion { Shape: var shape, Start: var startPose, Direction: var direction }:
                shapeHits.Clear();
                backend.CastShape(
                    frame: frame,
                    shape: shape,
                    start: DPose.Compose(toFrame, startPose),
                    direction: toFrame.Rotation.Rotate(direction),
                    startDistance: start,
                    endDistance: end,
                    filter: filter,
                    hits: shapeHits
                );
                foreach (ShapeCastHit found in shapeHits)
                    Keep(
                        new(
                            Entity: found.Entity,
                            Distance: found.Distance,
                            Point: toFrame.InverseTransformPoint(found.Point),
                            Normal: back.Rotate(found.Normal)
                        )
                    );
                break;
            case OverlapQuestion { Shape: var shape, Pose: var pose }:
                overlapped.Clear();
                backend.Overlap(
                    frame: frame,
                    shape: shape,
                    pose: DPose.Compose(toFrame, pose),
                    filter: filter,
                    entities: overlapped
                );
                foreach (Entity found in overlapped)
                    hits.Add(new(Entity: found, Distance: 0, Point: default, Normal: default));
                return;
            case null:
                throw new UnreachableException("Run refuses a request without a question before it walks.");
            default:
                throw new UnreachableException();
        }

        if (request.Question.FirstHitOnly && hits.Count > 1)
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

    /// <summary>Sorts the entities that an overlap found by entity index, and keeps each once.</summary>
    private void KeepEachEntityOnce()
    {
        Span<Hit> found = hits.AsSpan();
        found.Sort(ByDistanceThenEntity);
        int kept = 0;
        for (int i = 0; i < found.Length; i++)
            if (kept == 0 || found[i].Entity != found[kept - 1].Entity)
                found[kept++] = found[i];
        hits.Truncate(kept);
    }

    /// <summary>
    /// Queues the answers to <paramref name="request"/>: for a ray's nearest hit and a shape cast one answer, for every hit
    /// of a ray and an overlap one answer each, and a miss when there is no hit. Points and normals go into the frame that
    /// the asker is in now.
    /// </summary>
    private void Emit(in PhysicsRequest request, bool frameAlive)
    {
        PhysicsQuestion question = request.Question;
        FrameHandle askerFrame = entities.FrontRow(request.Asker).Frame;
        bool convert = frameAlive && !askerFrame.IsNull && askerFrame != request.Frame;
        DPose toAsker = convert ? frames.Relative(request.Frame, askerFrame) : DPose.Identity;
        int count = question.FirstHitOnly ? Math.Min(1, hits.Count) : hits.Count;
        for (int i = 0; i < count; i++)
        {
            ref Hit hit = ref hits[i];
            Double3 point = convert ? toAsker.TransformPoint(hit.Point) : hit.Point;
            Vector3 normal = convert ? toAsker.Rotation.Rotate(hit.Normal) : hit.Normal;
            router.AddEngineEvent(
                question switch
                {
                    RayQuestion => AnswerTo(
                        request.Asker,
                        new RayHit
                        {
                            Entity = hit.Entity,
                            Point = point,
                            Distance = (float)hit.Distance,
                            Tag = request.Tag,
                            Normal = normal,
                        }
                    ),
                    ShapeCastQuestion => AnswerTo(
                        request.Asker,
                        new ShapeHit
                        {
                            Entity = hit.Entity,
                            Point = point,
                            Distance = (float)hit.Distance,
                            Tag = request.Tag,
                            Normal = normal,
                        }
                    ),
                    OverlapQuestion => AnswerTo(
                        request.Asker,
                        new OverlapHit { Entity = hit.Entity, Tag = request.Tag }
                    ),
                    null => throw new UnreachableException(
                        "Run refuses a request without a question before it answers."
                    ),
                }
            );
        }

        if (count > 0)
            return;
        Double3 end = question.Origin + question.Direction * question.Length;
        Double3 missPoint = default;
        if (frameAlive)
            missPoint = convert ? toAsker.TransformPoint(end) : end;
        router.AddEngineEvent(
            question switch
            {
                RayQuestion => AnswerTo(
                    request.Asker,
                    new RayHit
                    {
                        Point = missPoint,
                        Distance = (float)question.Length,
                        Tag = request.Tag,
                    }
                ),
                ShapeCastQuestion => AnswerTo(
                    request.Asker,
                    new ShapeHit
                    {
                        Point = missPoint,
                        Distance = (float)question.Length,
                        Tag = request.Tag,
                    }
                ),
                OverlapQuestion => AnswerTo(request.Asker, new OverlapHit { Tag = request.Tag }),
                null => throw new UnreachableException("Run refuses a request without a question before it answers."),
            }
        );
    }

    /// <summary>The engine event that brings <paramref name="answer"/> to <paramref name="asker"/>.</summary>
    private static Event AnswerTo<T>(Entity asker, in T answer)
        where T : unmanaged, IEventPayload => Event.Create(target: new(asker), sender: Entity.Null, payload: answer);

    /// <summary>
    /// The filter of the request that runs: the request's entity query on the snapshot, around the asker, which it never
    /// accepts. During a visit to a parent, it also refuses the owner of the frame that the query came up from, whose hull
    /// the parent's system holds. The backend calls it inside Jolt, for each candidate body.
    /// </summary>
    private sealed class AskerFilter(World world) : IPhysicsQueryFilter
    {
        private Snapshot snapshot;
        private EntityQuery query;
        private Entity asker;
        private Entity skipped;

        /// <summary>How many candidates of the request named an entity that is not live, which only a bug produces.</summary>
        public int DeadHits { get; private set; }

        public void Begin(Entity askingEntity, in EntityQuery entityQuery)
        {
            asker = askingEntity;
            query = entityQuery;
            snapshot = new(world, askingEntity);
            skipped = Entity.Null;
            DeadHits = 0;
        }

        /// <summary>Makes the filter refuse <paramref name="entity"/> too, until the next call, or nothing more for null.</summary>
        public void Skip(Entity entity) => skipped = entity;

        public bool Accepts(Entity entity)
        {
            if (entity == asker || entity == skipped)
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
