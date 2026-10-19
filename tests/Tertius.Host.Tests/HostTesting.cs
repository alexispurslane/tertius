using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Host.Tests;

/// <summary>A payload type that only this test assembly declares, so that the runner must find it by its name.</summary>
public struct Nudge : IEventPayload
{
    public float Amount;
}

/// <summary>A payload with an entity field, which a script writes as a scene id.</summary>
public struct Tag : IEventPayload
{
    public Entity Other;
}

/// <summary>Two payload types with the same C# name, <c>Twin</c>, so that the name is ambiguous.</summary>
public static class TwinsA
{
    public struct Twin : IEventPayload;
}

/// <summary>The second <c>Twin</c>.</summary>
public static class TwinsB
{
    public struct Twin : IEventPayload;
}

/// <summary>A payload with the vector types that a script writes as arrays. 40 bytes.</summary>
public struct Aim : IEventPayload
{
    public Vector3 Direction;
    public Double3 Point;
}

/// <summary>A payload with the quaternion types that a script writes as arrays. 48 bytes.</summary>
public struct Spin : IEventPayload
{
    public Quaternion Turn;
    public DQuaternion Attitude;
}

/// <summary>A payload with a topic, which a script writes as its name, and a frame, which it writes as its owner's scene id.</summary>
public struct Link : IEventPayload
{
    public Topic Channel;
    public FrameHandle Frame;
}

/// <summary>Not a payload that an event can hold: it has a managed field, which the payload bytes cannot keep alive.</summary>
public struct Label : IEventPayload
{
    public string Text;
}

/// <summary>Not a payload that an event can hold: 64 bytes, more than the 48 of the payload bytes.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Size = 64)]
public struct Huge : IEventPayload;

/// <summary>Not a payload that an event can hold: a ref struct cannot be a type argument.</summary>
public ref struct Fleeting : IEventPayload;

/// <summary>A payload whose field System.Text.Json cannot read.</summary>
public struct Address : IEventPayload
{
    public nint Value;
}

/// <summary>A payload whose field a script cannot set, because it is read-only.</summary>
public readonly struct Fixed(float amount) : IEventPayload
{
    public readonly float Amount = amount;
}

/// <summary>Records every event of the types it wants, with the number of the tick it handled the event on.</summary>
public sealed class Probe(EventMask wants) : IComponent
{
    /// <summary>A probe that wants the test payloads and input.</summary>
    public Probe() : this(EventMask.Of<Nudge>().And<Tag>().And<InputEvent>().And<Aim>().And<Spin>().And<Link>())
    {
    }

    public int Priority => 0;
    public EventMask Wants => wants;

    /// <summary>Every event it handled, with <c>World.TickNumber</c> during the tick that handled it.</summary>
    public List<(long Tick, Event Event)> Seen { get; } = new();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        for (int i = 0; i < events.Count; i++)
            Seen.Add((self.Tick, events[i]));
    }
}

/// <summary>A component that throws on its first tick, so that the world records an error.</summary>
public sealed class ThrowsOnce : IComponent
{
    private bool thrown;

    public int Priority => 0;
    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (thrown)
            return;
        thrown = true;
        throw new InvalidOperationException("the probe broke on purpose");
    }
}

/// <summary>Puts one message into the outbox. A delegate of our own, because <see cref="Outbox"/> is a ref struct.</summary>
public delegate void SendFn(scoped ref Outbox outbox);

/// <summary>
/// Sends one message through its outbox on its first tick. It is not generic, because the engine refuses generic
/// component types: make one with <see cref="Of{T}"/>, which takes any payload type.
/// </summary>
public sealed class SendOnce : IComponent
{
    private readonly SendFn send;
    private bool sent;

    private SendOnce(SendFn send) => this.send = send;

    public int Priority => 0;
    public EventMask Wants => EventMask.Of<Tick>();

    /// <summary>A component that sends <paramref name="payload"/> to <paramref name="target"/> on its first tick.</summary>
    public static SendOnce Of<T>(Target target, T payload) where T : unmanaged, IEventPayload =>
        new((scoped ref Outbox outbox) => outbox.Send(target, payload));

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (sent)
            return;
        sent = true;
        send(ref outbox);
    }
}

/// <summary>Destroys its entity on its first tick.</summary>
public sealed class DestroysItselfOnFirstTick : IComponent
{
    public int Priority => 0;
    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => self.Destroy();
}

/// <summary>Writes one line to the world's log on its first tick.</summary>
public sealed class LogsOnce(string message) : IComponent
{
    private bool logged;

    public int Priority => 0;
    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (logged)
            return;
        logged = true;
        self.Log(message);
    }
}

/// <summary>Worlds and scenes for the tests of scripted runs.</summary>
public static class HostTesting
{
    public static DPose At(double x, double y = 0, double z = 0) => new(new Double3(x, y, z), DQuaternion.Identity);

    /// <summary>A world with no physics, one gameplay thread and the invariant checks of every tick.</summary>
    public static World NewWorld() => new(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true });

    /// <summary>A world on the real Jolt backend, which it owns, with one gameplay thread and the invariant checks of every tick.</summary>
    public static World NewJoltWorld() =>
        new(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, new JoltPhysicsBackend());

    private static FrameHandle Space(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));

    private static FrameHandle ChildFrame(World world, FrameHandle parent, DPose pose) =>
        world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = parent, Pose = pose, AsFrame = new FrameSpec(FrameBounds.Sphere(100)) }));

    /// <summary>
    /// The scene "probes", with no physics. In the root frame <c>space</c>: <c>probe</c>, which subscribes to the topic
    /// <c>test</c>, and <c>bystander</c>, which does not. In the frame <c>ship</c>, a child of space: <c>crew</c>. All three
    /// carry a <see cref="Probe"/>, which <see cref="ProbeOf"/> finds.
    /// </summary>
    public static SceneInstance Probes(World world)
    {
        var scene = new SceneInstance();
        FrameHandle space = Space(world);
        FrameHandle ship = ChildFrame(world, space, At(10));
        scene.Add("space", space);
        scene.Add("ship", ship);
        Entity probe = world.Spawn(new SpawnDesc { Frame = space, Components = [new Probe()] });
        world.Subscribe(probe, Topics.Get("test"));
        scene.Add("probe", probe);
        scene.Add("bystander", world.Spawn(new SpawnDesc { Frame = space, Pose = At(1), Components = [new Probe()] }));
        scene.Add("crew", world.Spawn(new SpawnDesc { Frame = ship, Components = [new Probe()] }));
        return scene;
    }

    public static readonly Scene ProbesScene = new("probes", (world, _) => Probes(world));

    /// <summary>The scene "logger": the root frame <c>space</c> and one entity that logs "hello from the logger" on its first tick.</summary>
    public static readonly Scene LoggerScene = new("logger", (world, _) =>
    {
        var scene = new SceneInstance();
        FrameHandle space = Space(world);
        scene.Add("space", space);
        world.Spawn(new SpawnDesc { Frame = space, Components = [new LogsOnce("hello from the logger")] });
        return scene;
    });

    /// <summary>The probe component of the entity with the scene id.</summary>
    public static Probe ProbeOf(World world, SceneInstance scene, string id) => world.GetComponent<Probe>(scene[id])!;

    /// <summary>
    /// The scene "ship-with-crate", with no physics. The root frame <c>space</c> holds the frame <c>ship</c> at (10, 0, 0),
    /// turned 90 degrees about +Y, which takes +X to -Z. The ship holds <c>crate</c> at (1, 0, 0), which moves at
    /// (1, 2, 3) m/s and turns at (0.25, 0.5, 0.75) rad/s in its first step, so it has these velocities after the first
    /// tick, and the frame <c>shuttle</c> at (0, 0, 2), which holds <c>pod</c> at (1, 0, 0). Space has a point gravity with G·M 1e9 m³/s², and it also holds the frame <c>moon</c> on a Kepler orbit around
    /// that G·M, and one entity with no scene id at (0, 5, 0).
    /// They are made in that order.
    /// </summary>
    public static SceneInstance ShipWithCrate(World world)
    {
        var scene = new SceneInstance();
        FrameHandle space = Space(world);
        // The moon takes its G·M from the point gravity of space. There is no physics, so nothing else feels it.
        world.SetFrameGravity(space, GravityField.Point(1e9));
        var turned = new DPose(new Double3(10, 0, 0), DQuaternion.FromAxisAngle(new Double3(0, 1, 0), Math.PI / 2));
        FrameHandle ship = ChildFrame(world, space, turned);
        scene.Add("space", space);
        scene.Add("ship", ship);
        // The crate has no body, so only its motion intents move it, and a spawn refuses a velocity on it. Its first motion
        // intent is the full sample of a steady drift and turn over its first step, so the first sync point gives it the
        // velocities of that sample.
        var crateVelocity = new Double3(1, 2, 3);
        var crateSpin = new Double3(0.25, 0.5, 0.75);
        double step = world.DeltaTime;
        var crateEnd = new DPose(At(1).Position + crateVelocity * step, DQuaternion.FromAxisAngle(crateSpin / crateSpin.Length, crateSpin.Length * step));
        scene.Add("crate", world.Spawn(new SpawnDesc
        {
            Frame = ship,
            Pose = At(1),
            FirstMove = new MotionIntent(new MotionSample(crateEnd, crateVelocity, crateVelocity, Double3.Zero, crateSpin, Double3.Zero)),
        }));
        FrameHandle shuttle = ChildFrame(world, ship, At(0, 0, 2));
        scene.Add("shuttle", shuttle);
        scene.Add("pod", world.Spawn(new SpawnDesc { Frame = shuttle, Pose = At(1) }));
        scene.Add("moon", world.Spawn(new SpawnDesc
        {
            Frame = space,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
        }.OnKeplerOrbit(world, KeplerOrbit.Circular(1000))));
        world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, 5) });
        return scene;
    }

    /// <summary>
    /// The scene "box-on-floor", which needs physics. The root frame <c>ground</c> has 9.81 m/s² of gravity along -Y.
    /// A static <c>floor</c> has its top at y = 0, and a 1 m <c>box</c> starts with its center at y = 1.5, so it falls
    /// 1 m and lands after about 0.45 s.
    /// </summary>
    public static SceneInstance BoxOnFloor(World world)
    {
        var scene = new SceneInstance();
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        scene.Add("ground", ground);
        scene.Add("floor", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
        }));
        scene.Add("box", world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 1.5), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10) }));
        return scene;
    }

    public static readonly Scene BoxOnFloorScene = new("box-on-floor", (world, _) => BoxOnFloor(world));

    /// <summary>A new empty folder under the system temp folder. The caller deletes it.</summary>
    public static string NewFolder() => Directory.CreateTempSubdirectory("tertius-host-tests-").FullName;

    /// <summary>The folder that holds Tertius.slnx, found by walking up from the test binaries.</summary>
    public static string RepositoryRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "Tertius.slnx")))
                return folder.FullName;
        throw new InvalidOperationException("Tertius.slnx is not above the test binaries.");
    }
}
