using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation.Tests;

// The shared helpers of this test project that are not components. The components are in Components/, one class per
// file. This file holds, in order: the payload types, TestWorld, the extensions over World, and the entity table helper
// for the subsystem tests. The physics is real Jolt: see TestWorld.OnJolt and FaultInjectingBackend.

// ---- Payloads ---------------------------------------------------------------------------------------------------

public struct Damage : IEventPayload
{
    public float Amount;
    public int Kind;
}

public struct Heal : IEventPayload
{
    public float Amount;
}

public struct Note : IEventPayload
{
    public int Value;
}

// ---- TestWorld ----------------------------------------------------------------------------------------------------

/// <summary>
/// A world for one test, with the settings that tests want: one thread (so that a run is deterministic and fast under
/// mutation testing), no physics unless the test gives a backend, <see cref="WorldOptions.DebugChecks"/> on, and a
/// root frame in which <see cref="SpawnWith"/> and <see cref="SpawnAt"/> spawn. Dispose it at the end of the test.
/// Take <see cref="World"/> for the members of the world itself. The static members are the helpers that need no
/// instance: <see cref="Create"/> for a test that wants a plain <see cref="Simulation.World"/>, and the pose helpers.
/// </summary>
public sealed class TestWorld : IDisposable
{
    /// <summary>A dynamic ball of radius 0.5, the body that most tests give an entity.</summary>
    public static readonly BodyDesc Ball = new(Shape: new SphereShapeDesc(0.5f));

    /// <param name="physics">The physics backend, or null for none. See <see cref="OnJolt"/>.</param>
    /// <param name="maxDegreeOfParallelism">The most gameplay workers. 1 runs every chunk on the ticking thread.</param>
    /// <param name="chunkSize">Entity slots per chunk. A small chunk size makes entities share fewer outboxes.</param>
    /// <param name="debugChecks">Runs the invariant checks of every subsystem at the end of every sync point. Turn it off for tests that measure allocations or time.</param>
    /// <param name="configure">Changes any other option before the world is created, such as
    /// <see cref="WorldOptions.SpawnPoolSize"/>.</param>
    public TestWorld(IPhysicsBackend? physics = null, int maxDegreeOfParallelism = 1, int chunkSize = 64,
        bool debugChecks = true,
        Action<WorldOptions>? configure = null)
    {
        World = Create(root: out FrameHandle root, maxDop: maxDegreeOfParallelism, physics: physics,
            chunkSize: chunkSize, debugChecks: debugChecks, configure: configure);
        Root = root;
    }

    /// <summary>The world under test.</summary>
    public World World { get; }

    /// <summary>The root frame, which is large enough to hold everything that a test puts in it (a sphere of radius 1e12).</summary>
    public FrameHandle Root { get; }

    /// <summary>
    /// A test world whose physics is the real Jolt backend behind a <see cref="FaultInjectingBackend"/>, with a root frame
    /// that has no gravity. With no edit set on <paramref name="faults"/>, the world behaves as it does on bare Jolt. The
    /// world disposes the backend. The other parameters are those of the constructor.
    /// </summary>
    public static TestWorld OnJolt(out FaultInjectingBackend faults, int maxDegreeOfParallelism = 1, int chunkSize = 64,
        bool debugChecks = true,
        Action<WorldOptions>? configure = null)
    {
        faults = new(new(new() { WorkerThreads = 2 }));
        return new(physics: faults, maxDegreeOfParallelism: maxDegreeOfParallelism, chunkSize: chunkSize,
            debugChecks: debugChecks, configure: options =>
            {
                options.DisposePhysics = true;
                configure?.Invoke(options);
            });
    }

    /// <summary>Spawns an entity with these components at the origin of <see cref="Root"/>. Outside a tick it is live at once.</summary>
    public Entity SpawnWith(params IComponent[] components) => World.SpawnWith(Root, components);

    /// <summary>Spawns an entity with these components at <paramref name="position"/> in <see cref="Root"/>.</summary>
    public Entity SpawnAt(Double3 position, params IComponent[] components) =>
        World.SpawnAt(Root, position, components);

    /// <summary>Spawns a frame that does not move, with the given pose in <paramref name="parent"/> (default:
    /// <see cref="Root"/>).</summary>
    public FrameHandle SpawnStaticFrame(DPose pose, float radius = 1000, FrameHandle parent = default) =>
        World.SpawnStaticFrame(parent.IsNull ? Root : parent, pose, radius);

    /// <summary>Runs this many ticks.</summary>
    public void Run(int ticks) => World.Run(ticks);

    /// <summary>
    /// Gives <see cref="Root"/> a point gravity with this G·M, in m³/s². A <see cref="KeplerMotion"/> of a frame in the root
    /// takes its G·M from it (see <see cref="Tertius.Frames.Rails.KeplerOrbit.MuFrom"/>), and so does everything that falls in
    /// the root with real physics. Call it before the first Kepler frame spawns, so that the spawn helpers place the frame
    /// with the same G·M (see <see cref="Tertius.Testing.RailsSpawn"/>), and only in a test that wants a Kepler frame in the
    /// root.
    /// </summary>
    public void GiveRootPointGravity(double mu) => World.SetFrameGravity(Root, GravityField.Point(mu));

    public void Dispose() => World.Dispose();

    /// <summary>
    /// Creates a world with the test settings and its root frame. This is the form for a test that wants the world
    /// and the root frame as separate variables. A <see cref="TestWorld"/> instance wraps the same call.
    /// </summary>
    public static World Create(out FrameHandle root, int maxDop = 1, IPhysicsBackend? physics = null,
        int chunkSize = 64,
        bool debugChecks = true, Action<WorldOptions>? configure = null)
    {
        var options = new WorldOptions
            { MaxDegreeOfParallelism = maxDop, ChunkSize = chunkSize, DebugChecks = debugChecks };
        configure?.Invoke(options);
        var world = new World(options, physics);
        root = world.CreateRootFrame(new(FrameBounds.Sphere(1e12f)));
        return world;
    }

    // ---- Poses ------------------------------------------------------------------------------------------------

    /// <summary>A pose at this position with no rotation.</summary>
    public static DPose At(double x, double y = 0, double z = 0) => new(new(x, y, z), DQuaternion.Identity);

    /// <summary>True if the two positions are at most <paramref name="tolerance"/> metres apart.</summary>
    public static bool RoughlyEqual(Double3 a, Double3 b, double tolerance = 1e-9) => (a - b).Length <= tolerance;

    public static int SizeOf<T>() => Unsafe.SizeOf<T>();
}

// ---- Extensions over World ------------------------------------------------------------------------------------

/// <summary>Spawn and run helpers for a <see cref="World"/> that a test holds directly, for example from
/// <see cref="TestWorld.Create"/>.</summary>
public static class WorldTestExtensions
{
    /// <summary>Spawns an entity with these components at the origin of <paramref name="frame"/>.</summary>
    public static Entity SpawnWith(this World world, FrameHandle frame, params IComponent[] components) =>
        world.Spawn(new() { Frame = frame, Components = components });

    /// <summary>Spawns an entity with these components at <paramref name="position"/> in <paramref name="frame"/>.</summary>
    public static Entity SpawnAt(this World world, FrameHandle frame, Double3 position,
        params IComponent[] components) =>
        world.Spawn(new() { Frame = frame, Pose = new(position, DQuaternion.Identity), Components = components });

    /// <summary>Spawns a frame that does not move, as a child of <paramref name="parent"/>, and returns its handle.</summary>
    public static FrameHandle SpawnStaticFrame(this World world, FrameHandle parent, DPose pose, float radius = 1000) =>
        world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = parent,
            Pose = pose,
            AsFrame = new(FrameBounds.Sphere(radius))
        }));

    /// <summary>Runs this many ticks.</summary>
    public static void Run(this World world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            world.Tick();
    }

    /// <summary>
    /// Edits the output of the step of tick <paramref name="tick"/> only, after Jolt wrote it. The wrapper runs the edit on the
    /// physics thread, in the step that its <see cref="FaultInjectingBackend.Steps"/> list names last.
    /// </summary>
    public static void EditOutputOfTick(this FaultInjectingBackend faults, long tick, Action<PhysicsStepOutput> edit) =>
        faults.EditOutput = output =>
        {
            if (faults.Steps[^1].Tick == tick)
                edit(output);
        };
}

// ---- Entity table for the subsystem tests ---------------------------------------------------------------------------

/// <summary>An entity table with live entities, for the subsystems that keep per-entity state in its columns.</summary>
internal static class TableSupport
{
    public static EntityTable NewTable()
    {
        EngineEvents.Register();
        return new(chunkSize: 4, initialCapacity: 8);
    }

    public static Entity SpawnLive(this EntityTable table, FrameHandle frame = default, double x = 0)
    {
        Entity entity = table.Reserve();
        table.Activate(entity: entity, row: new() { Frame = frame, PhysicsHome = frame, Pose = TestWorld.At(x) },
            render: new(), components: [], wants: [],
            body: null, bodyCenterOfMass: Vector3.Zero, ownedFrame: FrameHandle.Null);
        return entity;
    }
}
