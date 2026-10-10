using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation.Tests;

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

public delegate void HandleFn(Self self, scoped ref EventList events, scoped ref Outbox outbox);

/// <summary>A component whose behavior is a delegate, for tests.</summary>
public sealed class Lambda(EventMask wants, HandleFn handle, int priority = 0) : IComponent
{
    public int Priority => priority;
    public EventMask Wants => wants;
    public int Runs;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        Runs++;
        handle(self, ref events, ref outbox);
    }
}

/// <summary>Records every event it is called with, and the tick.</summary>
public sealed class Recorder : IComponent
{
    public Recorder(EventMask wants, int priority = 0)
    {
        Wants = wants;
        Priority = priority;
    }

    public Recorder() : this(EventMask.Of<Ping>())
    {
    }

    public int Priority { get; }
    public EventMask Wants { get; }
    public readonly List<(long Tick, Event Event)> Seen = new();
    public int Runs;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        Runs++;
        for (int i = 0; i < events.Count; i++)
            Seen.Add((self.Tick, events[i]));
    }

    public List<int> PingValues() => Seen.Where(s => s.Event.Is<Ping>()).Select(s => s.Event.As<Ping>().Value).ToList();

    public List<(long Tick, int Value)> Pings() =>
        Seen.Where(s => s.Event.Is<Ping>()).Select(s => (s.Tick, s.Event.As<Ping>().Value)).ToList();
}

/// <summary>Moves its bodiless entity by a fixed step every tick.</summary>
public sealed class Mover(Double3 step) : IComponent
{
    public int Priority => 0;
    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        ref DPose pose = ref self.Transform;
        pose = pose with { Position = self.Pose.Position + step };
    }
}

/// <summary>A deterministic, single-threaded stand-in for the Jolt backend.</summary>
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

    public readonly SortedDictionary<Entity, Body> Bodies = new();
    public readonly List<string> Changes = new();
    public readonly List<(long Tick, Entity Entity, PhysicsIntent Intent)> Intents = new();
    public readonly List<int> StepThreads = new();
    public readonly List<bool> StepOnPool = new();
    public readonly List<long> StepTicks = new();
    public readonly List<double> StepTimes = new();

    /// <summary>Called at the end of each step, on the physics thread, to add scripted results.</summary>
    public Action<PhysicsStepContext, PhysicsStepOutput>? OnStep;

    public bool Integrate { get; set; } = true;
    public bool Disposed { get; private set; }

    public void ApplyChanges(PhysicsChanges changes, FrameTree frames)
    {
        foreach (FrameHandle frame in changes.FramesCreated)
            Changes.Add($"frame+ {frame.Index}");
        foreach (Entity entity in changes.BodiesDestroyed)
        {
            Changes.Add($"body- {entity}");
            Bodies.Remove(entity);
        }
        foreach (FrameHandle frame in changes.FramesDestroyed)
            Changes.Add($"frame- {frame.Index}");
        foreach (BodyCreate create in changes.BodiesCreated)
        {
            Changes.Add($"body+ {create.Entity} in {create.Frame.Index}");
            Bodies[create.Entity] = new Body
            {
                Desc = create.Desc,
                Frame = create.Frame,
                Pose = create.Pose,
                Velocity = create.LinearVelocity,
                AngularVelocity = create.AngularVelocity,
            };
        }
    }

    public void Step(in PhysicsStepContext context, PhysicsStepOutput output)
    {
        StepThreads.Add(Environment.CurrentManagedThreadId);
        StepOnPool.Add(Thread.CurrentThread.IsThreadPoolThread);
        StepTicks.Add(context.Tick);
        StepTimes.Add(context.Time);
        float dt = context.DeltaTime;
        ReadOnlySpan<PhysicsIntent> intents = context.Intents.Span;
        foreach (Entity entity in context.IntentEntities.Span)
        {
            PhysicsIntent intent = intents[(int)entity.Index];
            Intents.Add((context.Tick, entity, intent));
            if (!Bodies.TryGetValue(entity, out Body? body))
                continue;
            float mass = body.Desc.Mass;
            body.Velocity += intent.Force / mass * dt + intent.Impulse / mass;
            if ((intent.Flags & IntentFlags.SetVelocity) != 0)
                body.Velocity = intent.Velocity;
            if ((intent.Flags & (IntentFlags.Teleport | IntentFlags.KinematicTarget)) != 0)
                body.Pose = intent.Target;
        }
        if (Integrate)
        {
            foreach ((Entity entity, Body body) in Bodies)
            {
                if (body.Desc.Motion == BodyMotion.Static)
                    continue;
                body.Pose = body.Pose with { Position = body.Pose.Position + (Double3)(body.Velocity * dt) };
                output.Bodies.Add(new BodyState(entity, body.Frame, body.Pose, body.Velocity, body.AngularVelocity));
            }
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

    public void Dispose() => Disposed = true;
}

public static class TestWorld
{
    public static readonly BodyDesc Ball = new(new SphereShapeDesc(0.5f));

    public static World Create(out FrameHandle root, int maxDop = 1, IPhysicsBackend? physics = null, int chunkSize = 64)
    {
        var world = new World(new WorldOptions { MaxDegreeOfParallelism = maxDop, ChunkSize = chunkSize, DebugChecks = true }, physics);
        root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e12f), MotionDriver.Static));
        return world;
    }

    public static Entity SpawnWith(this World world, FrameHandle frame, params IComponent[] components) =>
        world.Spawn(new SpawnDesc { Frame = frame, Components = components });

    public static Entity SpawnAt(this World world, FrameHandle frame, Double3 position, params IComponent[] components) =>
        world.Spawn(new SpawnDesc { Frame = frame, Pose = new DPose(position, DQuaternion.Identity), Components = components });

    public static void Run(this World world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            world.Tick();
    }

    public static DPose At(double x, double y = 0, double z = 0) => new(new Double3(x, y, z), DQuaternion.Identity);

    public static ulong Bits(double value) => (ulong)BitConverter.DoubleToInt64Bits(value);

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

    public static bool RoughlyEqual(Double3 a, Double3 b, double tolerance = 1e-9) => (a - b).Length <= tolerance;

    public static int SizeOf<T>() => Unsafe.SizeOf<T>();
}
