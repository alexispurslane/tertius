using System.Numerics;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using JoltPhysicsSharp;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>A component that only marks the entities that a ray of <see cref="RayFilterBenchmarks"/> may hit.</summary>
public sealed class RayMarker : IComponent
{
    public int Priority => 0;
    public EventMask Wants => default;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
    }
}

/// <summary>
/// Where should a ray cast test its entity query: inside Jolt, or on the hits afterwards (ter-juuk)? One nearest-hit cast along a
/// row of boxes, in the Jolt system of a backend's frame, with the filter of every variant built on the same
/// <see cref="EntityQuery.Matches"/> against the world's snapshot. The ray starts inside the asker's own sphere, so every
/// variant also skips the asker.
/// <list type="bullet">
/// <item>InsideJolt: an <see cref="ObjectLayerFilter"/> tests the content groups of each candidate's object layer, and a
/// <see cref="BodyFilter"/> maps each candidate to its entity and tests the query, so Jolt's closest-hit query returns
/// the nearest match. Two managed callbacks for each candidate, and a third for an accepted one.</item>
/// <item>BodyFilterOnly: only the <see cref="BodyFilter"/>, which tests the whole query, collision mask included, with
/// Jolt's closest-hit collector. One managed callback for each rejected candidate, and two for an accepted one.</item>
/// <item>SortedHitsThenFilter: no filter inside Jolt, every hit sorted by distance, then the query on the hits in order.</item>
/// <item>CollectorFilters: no filter inside Jolt, and a collector that tests the query on each hit as Jolt finds it and
/// narrows the ray to the nearest match so far. One managed callback for each hit.</item>
/// </list>
/// <see cref="OnlyFarthestMatches"/> false lets every box match, so the nearest box is the answer; true lets only the
/// farthest box match, so every box before it is a rejected hit.
/// </summary>
[MemoryDiagnoser]
public unsafe class RayFilterBenchmarks
{
    [Params(256)]
    public int Bodies;

    [Params(false, true)]
    public bool OnlyFarthestMatches;

    private World world = null!;
    private Entity asker;
    private Entity[] byIndex = null!;
    private NarrowPhaseQuery narrowPhase;
    private BodyInterface bodiesNoLock;
    private Snapshot snapshot;
    private EntityQuery query;
    private readonly List<RayCastResult> sorted = new(1024);
    private GroupFilter groupFilter = null!;
    private QueryBodyFilter bodyFilter = null!;
    private AllBroadPhaseLayers broadPhaseFilter = null!;
    private GCHandle self;

    private static readonly RVector3 Origin = new(0, 0, 0);
    private static readonly Vector3 Direction = new(1000, 0, 0);

    [GlobalSetup]
    public void Setup()
    {
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 });
        world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DisposePhysics = true }, backend);
        FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        asker = world.Spawn(new SpawnDesc
        {
            Frame = root,
            Pose = new DPose(Double3.Zero, DQuaternion.Identity),
            Body = new BodyDesc(new SphereShapeDesc(0.5f), BodyMotion.Static),
        });
        var boxes = new List<Entity> { asker };
        for (int i = 0; i < Bodies; i++)
        {
            boxes.Add(world.Spawn(new SpawnDesc
            {
                Frame = root,
                Pose = new DPose(new Double3(3 + 2 * i, 0, 0), DQuaternion.Identity),
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static),
                Components = i == Bodies - 1 ? [new RayMarker()] : [],
            }));
        }
        world.Tick();
        byIndex = new Entity[boxes.Max(static e => e.Index) + 1];
        foreach (Entity e in boxes)
            byIndex[e.Index] = e;

        PhysicsSystem system = backend.SystemOf(root)!;
        narrowPhase = system.NarrowPhaseQuery;
        bodiesNoLock = system.BodyInterfaceNoLock;
        snapshot = new Snapshot(world, asker);
        query = OnlyFarthestMatches ? EntityQuery.InAny(CollisionGroup.Solid).With<RayMarker>() : EntityQuery.InAny(CollisionGroup.Solid);
        groupFilter = new GroupFilter(query.Collision.AnyOf.Bits);
        bodyFilter = new QueryBodyFilter(this);
        broadPhaseFilter = new AllBroadPhaseLayers();
        self = GCHandle.Alloc(this);

        // Every variant must find the same box, or the comparison means nothing.
        float[] answers = [InsideJolt(), BodyFilterOnly(), SortedHitsThenFilter(), CollectorFilters()];
        if (answers.Any(a => a != answers[0]) || answers[0] <= 0)
            throw new InvalidOperationException($"The variants disagree: {string.Join(", ", answers)}.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        self.Free();
        groupFilter.Dispose();
        bodyFilter.Dispose();
        broadPhaseFilter.Dispose();
        world.Dispose();
    }

    private bool Accepts(BodyID body)
    {
        Entity entity = byIndex[(uint)bodiesNoLock.GetUserData(body)];
        return entity != asker && query.Matches(snapshot, entity, default);
    }

    [Benchmark(Baseline = true)]
    public float InsideJolt()
    {
        lock (JoltRuntime.NativeGate)
            return narrowPhase.CastRay(Origin, Direction, out RayCastResult hit, broadPhaseFilter, groupFilter, bodyFilter) ? hit.Fraction : -1;
    }

    [Benchmark]
    public float BodyFilterOnly()
    {
        lock (JoltRuntime.NativeGate)
        {
            sorted.Clear();
            narrowPhase.CastRay(Origin, Direction, new RayCastSettings(), CollisionCollectorType.ClosestHit, sorted, bodyFilter: bodyFilter);
            return sorted.Count > 0 ? sorted[0].Fraction : -1;
        }
    }

    [Benchmark]
    public float SortedHitsThenFilter()
    {
        lock (JoltRuntime.NativeGate)
        {
            sorted.Clear();
            narrowPhase.CastRay(Origin, Direction, new RayCastSettings(), CollisionCollectorType.AllHitSorted, sorted);
            foreach (RayCastResult hit in sorted)
                if (Accepts(hit.BodyID))
                    return hit.Fraction;
            return -1;
        }
    }

    // The nearest accepted fraction so far, for CollectorFilters.
    private float nearest;

    [Benchmark]
    public float CollectorFilters()
    {
        lock (JoltRuntime.NativeGate)
        {
            nearest = 1.0000001f;
            narrowPhase.CastRay(Origin, Direction, new RayCastSettings(), &OnHit, GCHandle.ToIntPtr(self));
            return nearest <= 1 ? nearest : -1;
        }
    }

    [UnmanagedCallersOnly]
    private static float OnHit(nint state, RayCastResult* hit)
    {
        var benchmark = (RayFilterBenchmarks)GCHandle.FromIntPtr(state).Target!;
        if (hit->Fraction < benchmark.nearest && benchmark.Accepts(hit->BodyID))
            benchmark.nearest = hit->Fraction;
        return benchmark.nearest;
    }

    /// <summary>Tests the content groups in the low 12 bits of each candidate's object layer, as the backend encodes them.</summary>
    private sealed class GroupFilter(uint anyOf) : ObjectLayerFilter
    {
        protected override bool ShouldCollide(ObjectLayer layer) => (layer.Value & anyOf) != 0;
    }

    private sealed class QueryBodyFilter(RayFilterBenchmarks benchmark) : BodyFilter
    {
        protected override bool ShouldCollide(BodyID bodyID) => benchmark.Accepts(bodyID);

        protected override bool ShouldCollideLocked(Body body) => true;
    }

    private sealed class AllBroadPhaseLayers : BroadPhaseLayerFilter
    {
        protected override bool ShouldCollide(BroadPhaseLayer layer) => true;
    }
}
