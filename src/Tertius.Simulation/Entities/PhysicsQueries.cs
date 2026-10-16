namespace Tertius.Simulation;

/// <summary>
/// The physics queries of the running entity, which <see cref="Self.Physics"/> gives. A component cannot ask Jolt anything
/// during its run, because the physics step of the same tick runs beside gameplay. So each query is a request: the sync
/// point of this tick runs it against Jolt and the snapshot of the end of the tick, and its answer arrives as an event in
/// this entity's inbox in the next tick. See docs/plans/queries-and-sensors.md, "Physics queries".
/// </summary>
/// <remarks>
/// <para>
/// A ray works in the frame that the entity is in now, <see cref="Self.Frame"/>: its origin and its direction are in that
/// frame's coordinates and axes. It crosses frame bubbles: into the frames whose bubbles it passes through, and out to the
/// parent frame when it leaves the bubble of its own. It skips every body of the entity that asked, and it can hit any body
/// of another entity that the query accepts: its own body, a frame's hull or interior, and a character. It ignores the
/// back faces of a mesh, so a ray that starts inside a mesh passes out of it, and it treats a convex shape as solid, so a
/// ray that starts inside one hits it at once.
/// </para>
/// <para>
/// The answer is a <see cref="RayHit"/> with <c>tag</c>, in the frame that the entity is in when the answer arrives.
/// A ray that hits nothing answers with a miss. The sync point runs the requests of a tick in entity order, and each
/// entity's in the order it made them, so the answers reach the inbox in that order. Hits at the same distance come in
/// entity order. If the frame of the ray is gone by the sync point, the answer is a miss with a zero point.
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
        world.RequestRay(chunk, entity, from, direction, length, query, tag, RayCastKind.Nearest);

    /// <summary>
    /// Like <see cref="CastRay"/>, but answers with one <see cref="RayHit"/> for every entity along the ray that
    /// <paramref name="query"/> matches, nearest first, each entity once. A ray that hits nothing answers with one miss.
    /// </summary>
    public void CastRayAll(Double3 from, Double3 direction, double length, in EntityQuery query, int tag) =>
        world.RequestRay(chunk, entity, from, direction, length, query, tag, RayCastKind.All);
}

/// <summary>A ray cast that an entity asked for during a tick, in the coordinates of <see cref="Frame"/>, with a unit direction.</summary>
internal readonly record struct RayRequest(
    Entity Asker, FrameHandle Frame, Double3 From, Double3 Direction, double Length, EntityQuery Query, int Tag, RayCastKind Kind);
