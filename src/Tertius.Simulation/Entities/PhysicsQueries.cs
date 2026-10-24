namespace Tertius.Simulation;

/// <summary>
/// The physics queries of the running entity, which <see cref="Self.Physics"/> gives. A component cannot ask Jolt anything
/// during its run, because the physics step of the same tick runs beside gameplay. So each query is a request: the sync
/// point of this tick runs it against Jolt and the snapshot of the end of the tick, and its answer arrives as an event in
/// this entity's inbox in the next tick. See docs/plans/queries-and-sensors.md, "Physics queries".
/// </summary>
/// <remarks>
/// <para>
/// A query works in the frame that the entity is in now, <see cref="Self.Frame"/>: its points, poses and directions are in
/// that frame's coordinates and axes. It crosses frame bubbles: into the frames whose bodies it can meet, and out to the
/// parent frame where it reaches out of the bubble of its own. It skips every body of the entity that asked, and it can hit
/// any body of another entity that the query accepts: its own body, a frame's hull or interior, and a character. A query
/// that starts inside a frame meets that frame's interior, never its hull. A query ignores the back faces of a mesh, so a
/// ray or a shape that starts inside a mesh passes out of it, and it treats a convex shape as solid, so a ray or a shape
/// that starts inside one hits it at once.
/// </para>
/// <para>
/// The answers are in the frame that the entity is in when they arrive. A cast that hits nothing answers with a miss, and
/// an overlap that finds nothing answers once with no entity. The sync point runs the requests of a tick in entity order,
/// and each entity's in the order it made them, so the answers reach the inbox in that order. Hits at the same distance
/// come in entity order. If the frame of a query is gone by the sync point, the answer is a miss with a zero point.
/// </para>
/// </remarks>
public readonly ref struct PhysicsQueries
{
    private readonly World world;
    private readonly ChunkState chunk;
    private readonly Entity entity;

    internal PhysicsQueries(World world, ChunkState chunk, Entity entity)
    {
        this.world = world;
        this.chunk = chunk;
        this.entity = entity;
    }

    /// <summary>
    /// Casts a ray from <paramref name="from"/> along <paramref name="direction"/> for <paramref name="length"/> meters,
    /// and answers in the next tick with one <see cref="RayHit"/>: the nearest entity that <paramref name="query"/> matches,
    /// or a miss. The direction need not be unit length. Throws <see cref="ArgumentException"/> for a point or a direction
    /// that is not finite, a zero direction or a length that is not finite and positive, and
    /// <see cref="InvalidOperationException"/> for an entity in no frame, the owner of a root frame.
    /// </summary>
    public void CastRay(Double3 from, Double3 direction, double length, in EntityQuery query, int tag) =>
        world.RequestRay(chunk: chunk, entity: entity, kind: RayCastKind.Nearest, from: from, direction: direction, length: length, query: query, tag: tag);

    /// <summary>
    /// Like <see cref="CastRay"/>, but answers with one <see cref="RayHit"/> for every entity along the ray that
    /// <paramref name="query"/> matches, nearest first, each entity once. A ray that hits nothing answers with one miss.
    /// </summary>
    public void CastRayAll(Double3 from, Double3 direction, double length, in EntityQuery query, int tag) =>
        world.RequestRay(chunk: chunk, entity: entity, kind: RayCastKind.All, from: from, direction: direction, length: length, query: query, tag: tag);

    /// <summary>
    /// Moves <paramref name="shape"/> from <paramref name="start"/> along <paramref name="direction"/> for
    /// <paramref name="length"/> meters, without turning it, and answers in the next tick with one <see cref="ShapeHit"/>:
    /// the first entity that the shape touches and that <paramref name="query"/> matches, or a miss. The shape's center is
    /// at <paramref name="start"/>'s position and its axes are <paramref name="start"/>'s axes. The direction need not be
    /// unit length, nor the rotation. Throws <see cref="ArgumentException"/> for a shape that
    /// <see cref="QueryShape.Problem"/> refuses, a pose or a direction that is not finite, a zero direction or rotation, or
    /// a length that is not finite and positive, and <see cref="InvalidOperationException"/> for an entity in no frame.
    /// </summary>
    public void CastShape(in QueryShape shape, in DPose start, Double3 direction, double length, in EntityQuery query,
        int tag) =>
        world.RequestShapeCast(chunk: chunk, entity: entity, shape: shape, start: start, direction: direction, length: length, query: query, tag: tag);

    /// <summary>
    /// Asks which entities have a body that overlaps <paramref name="shape"/> at <paramref name="pose"/>, and answers in
    /// the next tick with one <see cref="OverlapHit"/> for each entity that <paramref name="query"/> matches, in entity
    /// order, or with one <see cref="OverlapHit"/> with no entity when there is none. Throws
    /// <see cref="ArgumentException"/> for a shape that <see cref="QueryShape.Problem"/> refuses or a pose that is not
    /// finite or has a zero rotation, and <see cref="InvalidOperationException"/> for an entity in no frame.
    /// </summary>
    public void Overlap(in QueryShape shape, in DPose pose, in EntityQuery query, int tag) =>
        world.RequestOverlap(chunk: chunk, entity: entity, shape: shape, pose: pose, query: query, tag: tag);
}

/// <summary>
/// A physics query that an entity asked for during a tick: who asked, in which frame, what <see cref="Question"/> it asks,
/// which entities the query may hit and the tag of the answer. The question's points, poses and directions are in the
/// coordinates and axes of <see cref="Frame"/>, the frame that the asker was in when it asked.
/// </summary>
/// <param name="Asker">The entity that asked, and that gets the answer. Its own bodies are never hit.</param>
/// <param name="Frame">The frame that the asker was in.</param>
/// <param name="Question">A ray cast, a shape cast or an overlap test.</param>
/// <param name="Query">Which entities the query may hit.</param>
/// <param name="Tag">The tag that the answer carries.</param>
internal readonly record struct PhysicsRequest(
    Entity Asker,
    FrameHandle Frame,
    PhysicsQuestion Question,
    EntityQuery Query,
    int Tag);
