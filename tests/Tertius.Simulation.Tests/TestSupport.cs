using System.Numerics;
using System.Runtime.CompilerServices;
using NSubstitute;

namespace Tertius.Simulation.Tests;

// The shared helpers of this test project that are not components. The components are in Components/, one class per
// file. This file holds, in order: the payload types, TestWorld, the extensions over World, the entity table helper
// for the subsystem tests, the NSubstitute physics doubles, and the one hand-written physics fake.

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

public struct Ping : IEventPayload
{
    public int Value;
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
/// instance: <see cref="Create"/> for a test that wants a plain <see cref="Simulation.World"/>, and the pose and hash helpers.
/// </summary>
public sealed class TestWorld : IDisposable
{
    /// <summary>A dynamic ball of radius 0.5, the body that most tests give an entity.</summary>
    public static readonly BodyDesc Ball = new(new SphereShapeDesc(0.5f));

    /// <param name="physics">The physics backend, or null for none. See <see cref="TestPhysics.Create"/> and <see cref="FakePhysics"/>.</param>
    /// <param name="maxDegreeOfParallelism">The most gameplay workers. 1 runs every chunk on the ticking thread.</param>
    /// <param name="chunkSize">Entity slots per chunk. A small chunk size makes entities share fewer outboxes.</param>
    /// <param name="debugChecks">Runs the invariant checks of every subsystem at the end of every sync point. Turn it off for tests that measure allocations or time.</param>
    /// <param name="configure">Changes any other option before the world is created, such as <see cref="WorldOptions.SpawnPoolSize"/>.</param>
    public TestWorld(IPhysicsBackend? physics = null, int maxDegreeOfParallelism = 1, int chunkSize = 64, bool debugChecks = true,
        Action<WorldOptions>? configure = null)
    {
        World = Create(out FrameHandle root, maxDegreeOfParallelism, physics, chunkSize, debugChecks, configure);
        Root = root;
    }

    /// <summary>The world under test.</summary>
    public World World { get; }

    /// <summary>The root frame, which is large enough to hold everything that a test puts in it (a sphere of radius 1e12).</summary>
    public FrameHandle Root { get; }

    /// <summary>Spawns an entity with these components at the origin of <see cref="Root"/>. Outside a tick it is live at once.</summary>
    public Entity SpawnWith(params IComponent[] components) => World.SpawnWith(Root, components);

    /// <summary>Spawns an entity with these components at <paramref name="position"/> in <see cref="Root"/>.</summary>
    public Entity SpawnAt(Double3 position, params IComponent[] components) => World.SpawnAt(Root, position, components);

    /// <summary>Spawns a frame that does not move, with the given pose in <paramref name="parent"/> (default: <see cref="Root"/>).</summary>
    public FrameHandle SpawnStaticFrame(DPose pose, float radius = 1000, FrameHandle parent = default) =>
        World.SpawnStaticFrame(parent.IsNull ? Root : parent, pose, radius);

    /// <summary>Runs this many ticks.</summary>
    public void Run(int ticks) => World.Run(ticks);

    /// <summary>
    /// Gives <see cref="Root"/> a point gravity with this G·M, in m³/s². A Kepler frame in the root takes its G·M from it
    /// (see <see cref="FrameTree.KeplerMu"/>), and so does everything that falls in the root with real physics. Call it
    /// before the first Kepler frame spawns, and only in a test that wants a Kepler frame in the root.
    /// </summary>
    public void GiveRootPointGravity(double mu) => World.SetFrameGravity(Root, GravityField.Point(mu));

    public void Dispose() => World.Dispose();

    /// <summary>
    /// Creates a world with the test settings and its root frame. This is the form for a test that wants the world
    /// and the root frame as separate variables. A <see cref="TestWorld"/> instance wraps the same call.
    /// </summary>
    public static World Create(out FrameHandle root, int maxDop = 1, IPhysicsBackend? physics = null, int chunkSize = 64,
        bool debugChecks = true, Action<WorldOptions>? configure = null)
    {
        var options = new WorldOptions { MaxDegreeOfParallelism = maxDop, ChunkSize = chunkSize, DebugChecks = debugChecks };
        configure?.Invoke(options);
        var world = new World(options, physics);
        root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e12f), MotionDriver.Static));
        return world;
    }

    // ---- Poses and hashes -------------------------------------------------------------------------------------

    /// <summary>A pose at this position with no rotation.</summary>
    public static DPose At(double x, double y = 0, double z = 0) => new(new Double3(x, y, z), DQuaternion.Identity);

    public static ulong Bits(double value) => (ulong)BitConverter.DoubleToInt64Bits(value);

    // The Mix functions fold values into a hash. The determinism and invariant tests compare hashes of whole runs, so
    // a change of one bit in one value, or of the order of two values, changes the result.
    public static ulong Mix(ulong hash, ulong value)
    {
        hash ^= value + 0x9E3779B97F4A7C15UL + (hash << 6) + (hash >> 2);
        return hash * 0xBF58476D1CE4E5B9UL;
    }

    public static ulong Mix(ulong hash, double value) => Mix(hash, Bits(value));

    public static ulong Mix(ulong hash, in DPose pose)
    {
        hash = Mix(hash, pose.Position.X);
        hash = Mix(hash, pose.Position.Y);
        hash = Mix(hash, pose.Position.Z);
        hash = Mix(hash, pose.Rotation.X);
        hash = Mix(hash, pose.Rotation.Y);
        hash = Mix(hash, pose.Rotation.Z);
        return Mix(hash, pose.Rotation.W);
    }

    public static ulong Mix(ulong hash, Vector3 v) => Mix(Mix(Mix(hash, v.X), v.Y), v.Z);

    public static ulong MixPayload(ulong hash, in Event e)
    {
        ReadOnlySpan<ulong> words = e.Payload;
        foreach (ulong word in words)
            hash = Mix(hash, word);
        return hash;
    }

    /// <summary>True if the two positions are at most <paramref name="tolerance"/> metres apart.</summary>
    public static bool RoughlyEqual(Double3 a, Double3 b, double tolerance = 1e-9) => (a - b).Length <= tolerance;

    public static int SizeOf<T>() => Unsafe.SizeOf<T>();
}

// ---- Extensions over World ------------------------------------------------------------------------------------

/// <summary>Spawn and run helpers for a <see cref="World"/> that a test holds directly, for example from <see cref="TestWorld.Create"/>.</summary>
public static class WorldTestExtensions
{
    /// <summary>Spawns an entity with these components at the origin of <paramref name="frame"/>.</summary>
    public static Entity SpawnWith(this World world, FrameHandle frame, params IComponent[] components) =>
        world.Spawn(new SpawnDesc { Frame = frame, Components = components });

    /// <summary>Spawns an entity with these components at <paramref name="position"/> in <paramref name="frame"/>.</summary>
    public static Entity SpawnAt(this World world, FrameHandle frame, Double3 position, params IComponent[] components) =>
        world.Spawn(new SpawnDesc { Frame = frame, Pose = new DPose(position, DQuaternion.Identity), Components = components });

    /// <summary>Spawns a frame that does not move, as a child of <paramref name="parent"/>, and returns its handle.</summary>
    public static FrameHandle SpawnStaticFrame(this World world, FrameHandle parent, DPose pose, float radius = 1000) =>
        world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = parent,
            Pose = pose,
            AsFrame = new FrameSpec(FrameBounds.Sphere(radius), MotionDriver.Static),
        }));

    /// <summary>Runs this many ticks.</summary>
    public static void Run(this World world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            world.Tick();
    }
}

// ---- Entity table for the subsystem tests ---------------------------------------------------------------------------

/// <summary>An entity table with live entities, for the subsystems that keep per-entity state in its columns.</summary>
internal static class TableSupport
{
    public static EntityTable NewTable()
    {
        EngineEvents.Register();
        return new EntityTable(chunkSize: 4, initialCapacity: 8);
    }

    public static Entity SpawnLive(this EntityTable table, FrameHandle frame = default, double x = 0)
    {
        Entity entity = table.Reserve();
        table.Activate(entity, new TransformRow { Frame = frame, PhysicsHome = frame, Pose = TestWorld.At(x) }, new RenderData(), [], [], null, FrameHandle.Null);
        return entity;
    }
}

// ---- Physics doubles: NSubstitute --------------------------------------------------------------------------------------

/// <summary>One physics step, as the backend saw it. It was recorded on the physics thread.</summary>
/// <param name="Tick">The number of the tick the step belongs to.</param>
/// <param name="Time">The simulation time at the start of the step, in seconds.</param>
/// <param name="ThreadId">The managed id of the thread that ran the step.</param>
/// <param name="OnPoolThread">Whether that thread is a thread-pool thread.</param>
public readonly record struct StepRecord(long Tick, double Time, int ThreadId, bool OnPoolThread);

/// <summary>The intent that one entity had written when a step began.</summary>
/// <param name="Tick">The number of the tick the step belongs to.</param>
/// <param name="Entity">The entity that wrote the intent during the previous tick.</param>
/// <param name="Intent">A copy of what it wrote.</param>
public readonly record struct IntentRecord(long Tick, Entity Entity, PhysicsIntent Intent);

/// <summary>
/// A copy of one <see cref="PhysicsChanges"/> batch, made when the world handed it to the backend. The world clears and
/// reuses its batch object right after the call, so a <c>Received()</c> check made later would see an empty batch.
/// The fields are those of <see cref="PhysicsChanges"/>. The backend applies a batch in this order: bodies destroyed,
/// frames destroyed, bodies created.
/// </summary>
public readonly record struct ChangeBatch(Entity[] BodiesDestroyed, FrameHandle[] FramesDestroyed, BodyCreate[] BodiesCreated, FrameHandle[] FramesCreated);

/// <summary>
/// Physics backends made with NSubstitute. A substitute does nothing: it moves no body and reports nothing, except what
/// a test scripts with <see cref="OnStep"/>. That suits a test that says "physics reported this" and then checks what the
/// world did with it. A test that needs bodies that move, or that needs the backend to remember what it was told to
/// create, uses <see cref="FakePhysics"/> instead.
/// </summary>
/// <remarks>
/// Each method here adds a callback with <c>When(...).Do(...)</c>, and NSubstitute runs every callback of a call, in the
/// order they were added. A script added with <see cref="OnStep"/> therefore runs together with the recorders, not
/// instead of them. Add each one once. The callbacks run on the physics thread. A test reads what they recorded after
/// <see cref="World.Tick"/> has returned, which waits for the step to end.
/// </remarks>
public static class TestPhysics
{
    /// <summary>
    /// A backend that does nothing. It can build every shape that <see cref="ShapeDesc.Problem"/> accepts. With
    /// <paramref name="onStep"/>, it runs that script at every step.
    /// </summary>
    public static IPhysicsBackend Create(Action<PhysicsStepContext, PhysicsStepOutput>? onStep = null)
    {
        IPhysicsBackend physics = Substitute.For<IPhysicsBackend>();
        physics.ShapeProblem(Arg.Any<ShapeDesc>()).Returns(call => call.Arg<ShapeDesc>().Problem());
        if (onStep != null)
            physics.OnStep(onStep);
        return physics;
    }

    /// <summary>
    /// Runs <paramref name="script"/> at every step, on the physics thread. It adds to the output that the world reads
    /// at the sync point, which starts empty. An exception that it throws is what the world sees as a failed step.
    /// </summary>
    public static void OnStep(this IPhysicsBackend physics, Action<PhysicsStepContext, PhysicsStepOutput> script) =>
        physics.When(p => p.Step(Arg.Any<PhysicsStepContext>(), Arg.Any<PhysicsStepOutput>()))
            .Do(call => script(call.Arg<PhysicsStepContext>(), call.Arg<PhysicsStepOutput>()));

    /// <summary>Records the tick, time and thread of every step.</summary>
    public static List<StepRecord> RecordSteps(this IPhysicsBackend physics)
    {
        var steps = new List<StepRecord>();
        physics.OnStep((context, _) => steps.Add(new StepRecord(
            context.Tick, context.Time, Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread)));
        return steps;
    }

    /// <summary>Records, at every step, the intent of each entity that wrote one during the previous tick.</summary>
    public static List<IntentRecord> RecordIntents(this IPhysicsBackend physics)
    {
        var intents = new List<IntentRecord>();
        physics.OnStep((context, _) =>
        {
            foreach (Entity entity in context.IntentEntities.Span)
                intents.Add(new IntentRecord(context.Tick, entity, context.IntentOf(entity)));
        });
        return intents;
    }

    /// <summary>
    /// Records a copy of every batch of structural changes that the world applies. The first batch also holds the
    /// root frame and everything spawned before the first tick.
    /// </summary>
    public static List<ChangeBatch> RecordChanges(this IPhysicsBackend physics)
    {
        var batches = new List<ChangeBatch>();
        physics.When(p => p.ApplyChanges(Arg.Any<PhysicsChanges>(), Arg.Any<FrameTree>()))
            .Do(call =>
            {
                var changes = call.Arg<PhysicsChanges>();
                batches.Add(new ChangeBatch(
                    [.. changes.BodiesDestroyed], [.. changes.FramesDestroyed], [.. changes.BodiesCreated], [.. changes.FramesCreated]));
            });
        return batches;
    }

    /// <summary>The intent that <paramref name="entity"/> wrote during the previous tick. Meaningful only for an entity in <see cref="PhysicsStepContext.IntentEntities"/>.</summary>
    public static PhysicsIntent IntentOf(this PhysicsStepContext context, Entity entity) => context.Intents.Span[(int)entity.Index];
}

// ---- Physics fake: hand-written ----------------------------------------------------------------------------------------

/// <summary>
/// A deterministic, single-threaded stand-in for the Jolt backend that keeps state: it remembers the bodies it was told
/// to create, moves every non-static body each step by its velocity, and moves a body to its new frame when the step
/// hands it off. It stays hand-written because NSubstitute cannot express that. A substitute only records calls and
/// returns what it is told to return; it holds no bodies between calls. A test that needs bodies to move, or that reads a
/// body back after a step, would have to re-implement all of this inside <c>When(...).Do(...)</c>. A test that only scripts
/// what physics reports, or only checks what the world told the backend, uses <see cref="TestPhysics"/>.
/// </summary>
public sealed class FakePhysics : IPhysicsBackend
{
    public sealed class Body
    {
        public required BodyDesc Desc;
        public FrameHandle Frame;
        public DPose Pose;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
    }

    /// <summary>The bodies it holds, by entity, in entity order so that every step visits them in the same order.</summary>
    public readonly SortedDictionary<Entity, Body> Bodies = new();

    /// <summary>Called at the end of each step, on the physics thread, to add scripted results.</summary>
    public Action<PhysicsStepContext, PhysicsStepOutput>? OnStep;

    public void ApplyChanges(PhysicsChanges changes, FrameTree frames)
    {
        foreach (Entity entity in changes.BodiesDestroyed)
            Bodies.Remove(entity);
        foreach (BodyCreate create in changes.BodiesCreated)
        {
            Bodies[create.Entity] = new Body
            {
                Desc = create.Desc,
                Frame = create.Frame,
                Pose = create.Pose,
                Velocity = create.LinearVelocity.ToVector3(),
                AngularVelocity = create.AngularVelocity,
            };
        }
    }

    public void Step(in PhysicsStepContext context, PhysicsStepOutput output)
    {
        float dt = context.DeltaTime;
        ReadOnlySpan<PhysicsIntent> intents = context.Intents.Span;
        foreach (Entity entity in context.IntentEntities.Span)
        {
            PhysicsIntent intent = intents[(int)entity.Index];
            if (!Bodies.TryGetValue(entity, out Body? body))
                continue;
            float mass = body.Desc.Mass;
            body.Velocity += intent.Force / mass * dt + intent.Impulse / mass;
            if ((intent.Flags & IntentFlags.SetVelocity) != 0)
                body.Velocity = intent.Velocity.ToVector3();
            if ((intent.Flags & IntentFlags.Teleport) != 0)
                body.Pose = intent.TeleportPose;
            if ((intent.Flags & IntentFlags.KinematicTarget) != 0)
                body.Pose = intent.Target;
        }
        foreach ((Entity entity, Body body) in Bodies)
        {
            if (body.Desc.Motion == BodyMotion.Static)
                continue;
            body.Pose = body.Pose with { Position = body.Pose.Position + (Double3)(body.Velocity * dt) };
            output.Bodies.Add(new BodyState(entity, body.Frame, body.Pose, body.Velocity, body.AngularVelocity));
        }
        OnStep?.Invoke(context, output);

        // Like a real backend, move handed-off bodies to their new frame and report them there.
        foreach (HandOff handOff in output.HandOffs)
        {
            if (!Bodies.TryGetValue(handOff.Entity, out Body? moved))
                continue;
            moved.Frame = handOff.To;
            moved.Pose = handOff.Pose;
            moved.Velocity = handOff.LinearVelocity;
            moved.AngularVelocity = handOff.AngularVelocity;
            int index = output.Bodies.FindIndex(b => b.Entity == handOff.Entity);
            if (index >= 0)
                output.Bodies[index] = new BodyState(handOff.Entity, handOff.To, handOff.Pose, handOff.LinearVelocity, handOff.AngularVelocity);
        }
    }

    public float BoundingRadius(ShapeDesc shape) => shape switch
    {
        SphereShapeDesc s => s.Radius,
        BoxShapeDesc b => b.HalfExtents.Length(),
        _ => 1,
    };

    public string? ShapeProblem(ShapeDesc shape) => shape.Problem();

    public void Dispose()
    {
    }
}
