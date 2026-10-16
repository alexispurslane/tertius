using System.Numerics;
using System.Runtime.ExceptionServices;
using JoltPhysicsSharp;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    // The one Jolt body filter of every query, created with the first cast and freed in Dispose. The binding's filter
    // classes leave OwnsHandle false, so NativeOwnership makes Dispose free the native filter.
    private QueryBodyFilter? queryBodyFilter;
    private readonly List<RayCastResult> rayResults = new(16);

    /// <inheritdoc/>
    /// <remarks>
    /// The whole query runs inside Jolt, in one <see cref="BodyFilter"/>: Jolt asks it about each candidate body before it
    /// tests the body's shape, and it maps the body's user data to the entity and asks <paramref name="filter"/>. So Jolt's
    /// closest-hit collector returns the nearest body that the filter accepts. The bench in
    /// benchmarks/Tertius.Benchmarks/RayFilterBenchmarks.cs chose this over an <see cref="ObjectLayerFilter"/> for the
    /// collision groups, which adds a managed callback for each candidate, and over testing the hits after an unfiltered
    /// cast, which tests the shape of every body along the ray. The normal of each hit comes from the body under a read
    /// lock, through <see cref="JoltNative"/>, so that no managed body object is made.
    /// </remarks>
    public void CastRay(FrameHandle frame, Double3 origin, Double3 direction, double start, double end, IPhysicsQueryFilter filter,
        RayCastKind kind, List<RayCastHit> hits)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(hits);
        if (!(end > start) || ExistingWorld(frame) is not { } world)
            return;
        lock (JoltRuntime.NativeGate)
        {
            QueryBodyFilter bodyFilter = queryBodyFilter ??= NativeOwnership.Owned(new QueryBodyFilter(this));
            double length = end - start;
            RVector3 from = (origin + direction * start).ToJolt();
            Vector3 ray = (direction * length).ToVector3();
            rayResults.Clear();
            bodyFilter.Begin(world.System.BodyInterfaceNoLock, filter);
            try
            {
                world.System.NarrowPhaseQuery.CastRay(from, ray, new RayCastSettings(),
                    kind == RayCastKind.Nearest ? CollisionCollectorType.ClosestHit : CollisionCollectorType.AllHitSorted, rayResults, bodyFilter: bodyFilter);
            }
            finally
            {
                bodyFilter.End();
            }

            BodyInterface bodies = world.System.BodyInterfaceNoLock;
            BodyLockInterface locks = world.System.BodyLockInterface;
            foreach (RayCastResult result in rayResults)
            {
                double distance = start + result.Fraction * length;
                Entity entity = EntityOf(bodies.GetUserData(result.BodyID));
                hits.Add(new RayCastHit(entity, distance, SurfaceNormal(locks, result, origin + direction * distance)));
            }
        }
    }

    /// <summary>The entity that a body's user data names, or null when no live record of the backend matches it.</summary>
    private Entity EntityOf(ulong userData) =>
        BodyUserData.Kind(userData) != BodyKind.None && BodyUserData.Index(userData) < (uint)records.Length &&
        records[BodyUserData.Index(userData)] is { } record && BodyUserData.Names(userData, record.Entity)
            ? record.Entity
            : Entity.Null;

    /// <summary>The unit normal of the surface that <paramref name="result"/> hit, at <paramref name="point"/> in the system's coordinates.</summary>
    private static unsafe Vector3 SurfaceNormal(BodyLockInterface locks, in RayCastResult result, Double3 point)
    {
        locks.LockRead(result.BodyID, out BodyLockRead read);
        Vector3 normal = default;
        if (read.Succeeded)
        {
            RVector3 position = point.ToJolt();
            JoltNative.SurfaceNormal(read.BodyHandle, result.subShapeID2, &position, &normal);
        }
        locks.UnlockRead(read);
        return normal;
    }

    /// <summary>
    /// The body filter of the queries. Jolt calls it from inside a query, through a managed callback, so an exception may
    /// not leave it: the first one is kept, every later candidate is refused, and <see cref="End"/> throws it again once
    /// Jolt has returned. A broken contract is a bug, so it is thrown again the same way, never swallowed.
    /// </summary>
    /// <remarks>
    /// For each candidate that <see cref="ShouldCollide"/> accepts, the binding hands <see cref="ShouldCollideLocked"/> a
    /// managed <see cref="Body"/>. It finds that object in a table of weak references, and it makes a new one when the
    /// garbage collector took the last, so a cast after a collection would allocate. The filter holds every body object
    /// that it is handed, which keeps every later lookup of the same body from allocating. A system that is disposed or
    /// grows frees its bodies, so the backend then calls <see cref="ForgetBodies"/>, which bounds the set.
    /// </remarks>
    private sealed class QueryBodyFilter(JoltPhysicsBackend backend) : BodyFilter
    {
        private readonly HashSet<Body> bodiesHandedIn = new(ReferenceEqualityComparer.Instance);
        private BodyInterface bodies;
        private IPhysicsQueryFilter? filter;
        private ExceptionDispatchInfo? failure;

        /// <summary>Lets go of the body objects that the binding handed in, after a system freed its bodies.</summary>
        public void ForgetBodies() => bodiesHandedIn.Clear();

        public void Begin(BodyInterface bodiesNoLock, IPhysicsQueryFilter queryFilter)
        {
            Contract.Requires(filter is null && failure is null);
            bodies = bodiesNoLock;
            filter = queryFilter;
        }

        /// <summary>Ends the query and throws the exception that the filter threw during it, if any.</summary>
        public void End()
        {
            ExceptionDispatchInfo? thrown = failure;
            filter = null;
            failure = null;
            thrown?.Throw();
        }

        protected override bool ShouldCollide(BodyID bodyID)
        {
            if (failure is not null || filter is null)
                return false;
            try
            {
                // Jolt has not locked the body yet, and nothing else runs at the sync point, so the read needs no lock.
                Entity entity = backend.EntityOf(bodies.GetUserData(bodyID));
                return !entity.IsNull && filter.Accepts(entity);
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
                return false;
            }
        }

        protected override bool ShouldCollideLocked(Body body)
        {
            bodiesHandedIn.Add(body);
            return true;
        }
    }
}
