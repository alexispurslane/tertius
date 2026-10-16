using System.Numerics;

namespace Tertius;

/// <summary>
/// Which entities a physics query may hit. The backend asks it about the entity of each candidate body, inside the
/// query, on the thread that runs the query, before it tests the body's shape. So it must be fast, and it must not call
/// the backend. An exception that it throws stops the query: the backend rejects every later candidate, and throws the
/// exception again from the query once the native query has returned.
/// </summary>
public interface IPhysicsQueryFilter
{
    bool Accepts(Entity entity);
}

/// <summary>What a ray cast of <see cref="IPhysicsBackend.CastRay"/> reports.</summary>
public enum RayCastKind : byte
{
    /// <summary>The nearest hit that the filter accepts, if any.</summary>
    Nearest,
    /// <summary>Every hit that the filter accepts, by distance. A body can be hit more than once, for example a mesh.</summary>
    All,
}

/// <summary>
/// One hit of a ray cast in one frame's physics system. <see cref="Distance"/> is along the ray from its origin, in
/// meters, and <see cref="Normal"/> is the unit normal of the hit surface in the axes of that frame.
/// </summary>
public readonly record struct RayCastHit(Entity Entity, double Distance, Vector3 Normal);
