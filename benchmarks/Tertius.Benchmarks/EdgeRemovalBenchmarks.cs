using System.Numerics;
using BenchmarkDotNet.Attributes;
using JoltPhysicsSharp;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of Jolt's enhanced internal edge removal on the floor of a ring (ter-fb3w). One tick of a world in which 200
/// boxes and 200 balls rest and slide on the floor of a static ring, in uniform gravity of 1 g along +X, so the floor's
/// lowest line runs along the ring's axis at the angle 0. The ring is the demo's <c>RevolvedSolid.Ring(20, 20.5, 5, 256)</c>,
/// built here the same way, because this project does not reference the demo: 256 segments of 20 m to 20.5 m from the
/// axis and 10 m along it, so each facet of the floor is 0.49 m wide, and a body that slides 1 m crosses two joints.
/// <list type="bullet">
/// <item><see cref="Floor"/> is the ring's collision shape: <see cref="RingFloor.Hulls"/>, the demo's compound of one
/// convex hull per segment, or <see cref="RingFloor.Mesh"/>, Jolt's mesh shape of the demo's mesh of the ring.</item>
/// <item><see cref="EdgeRemoval"/> sets Jolt's <c>EnhancedInternalEdgeRemoval</c> on the ring's body, or clears it,
/// after the backend has created the body. Jolt removes the internal edges of a pair when either of its bodies has the
/// flag, and the balls and boxes are convex, so the ring's flag decides for every pair.</item>
/// </list>
/// The bodies stand in 20 lanes along the floor, 0.5 m apart along the axis, each lane all boxes of 0.3 m and 10 kg or
/// all balls of 0.3 m across and 1 kg. In half of the lanes the bodies slide: each is 0.45 m from the next, and a
/// <see cref="Shuttle"/> holds the speed of each along the floor at 1 m/s and turns it every second, so a lane moves
/// together and crosses two joints each way. In the other half they rest: boxes 0.45 m apart, which friction holds on
/// the floor up to 12° from its lowest line, and balls in a touching row about the lowest line, which each other hold.
/// All of them fall asleep, so 200 bodies are awake. On the hulls without the flag, a sliding box trips at every joint
/// and tumbles.
/// <para>
/// Measured on 2026-10-19 in Release, on a machine that other work loaded to about 6 of its 10 cores, so one run of
/// BenchmarkDotNet varies by about 10 %. The mean tick of four runs of 20 or 30 iterations: on the hulls, 697 µs
/// without the flag and 613 µs with it (-12 %, from -21 % to +4 % in single runs); on the mesh, 591 µs without and
/// 564 µs with it (-5 %, from -10 % to +5 %). The scenes ticked in turn (<c>--edges 5000</c>, see
/// <see cref="Interleaved"/>), three runs: on the hulls -19 %, -24 % and -23 %, on the mesh +0.5 %, -4.0 % and -4.5 %.
/// So the flag costs nothing that these runs can see. It saves time on the hulls, where without it the boxes tumble.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class EdgeRemovalBenchmarks
{
    public enum RingFloor
    {
        Hulls,
        Mesh,
    }

    private const int Segments = 256;
    private const float Inner = 20, Outer = 20.5f, HalfWidth = 5;
    private const int Lanes = 20, PerLane = 20;
    private const float Half = 0.15f;

    /// <summary>
    /// Holds the speed of its body along the ring's floor, about the ring's axis, at 1 m/s toward growing angle for 60
    /// ticks and then the other way for 60, and leaves the rest of its velocity to Jolt.
    /// </summary>
    private sealed class Shuttle : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tertius.Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            Double3 p = self.PoseIn(self.Frame).Position;
            double theta = Math.Atan2(-p.Z, p.X);
            var along = new Double3(-Math.Sin(theta), 0, -Math.Cos(theta));
            double speed = self.Tick / 60 % 2 == 0 ? 1 : -1;
            Double3 v = self.VelocityIn(self.Frame);
            self.Body.SetVelocity(v - Double3.Dot(v, along) * along + speed * along);
        }
    }

    [Params(RingFloor.Hulls, RingFloor.Mesh)]
    public RingFloor Floor;

    [Params(false, true)]
    public bool EdgeRemoval;

    private World world = null!;

    [GlobalSetup]
    public void Setup()
    {
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 });
        world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DisposePhysics = true }, backend);
        FrameHandle frame = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform(new Vector3(9.81f, 0, 0))));
        world.Spawn(new SpawnDesc { Frame = frame, Body = new BodyDesc(Floor == RingFloor.Hulls ? RingHulls() : RingMesh(), BodyMotion.Static) });
        var box = new BoxShapeDesc(new Vector3(Half));
        var ball = new SphereShapeDesc(Half);
        for (int lane = 0; lane < Lanes; lane++)
        {
            bool boxes = lane % 2 == 0, slides = lane / 2 % 2 == 0;
            double y = (lane - (Lanes - 1) / 2.0) * 0.5;
            // A row of resting balls touches, so that each holds the next on the curved floor.
            double spacing = !slides && !boxes ? 2 * Half + 0.001 : 0.45;
            for (int i = 0; i < PerLane; i++)
            {
                double theta = (i - (PerLane - 1) / 2.0) * spacing / Inner;
                world.Spawn(new SpawnDesc
                {
                    Frame = frame,
                    Pose = OnFloor(theta, y),
                    Body = new BodyDesc(boxes ? box : ball, Mass: boxes ? 10 : 1),
                    Components = slides ? [new Shuttle()] : [],
                });
            }
        }
        // The first tick creates the bodies.
        world.Tick();
        SetEdgeRemoval(backend, frame, EdgeRemoval);
        // Two seconds: the resting boxes fall asleep, and the sliding lanes have turned twice.
        for (int i = 0; i < 120; i++)
            world.Tick();

        List<JoltPhysicsBackend.BodyFacts> facts = backend.BodiesIn(frame);
        int awake = facts.Count(body => body.Active && body.Motion == MotionType.Dynamic);
        Console.WriteLine($"// {Floor}, edge removal {EdgeRemoval}: {awake} of {Lanes * PerLane} bodies are awake.");
    }

    /// <summary>
    /// Sets or clears <c>EnhancedInternalEdgeRemoval</c> on the ring's body, the one static body in
    /// <paramref name="frame"/>'s system.
    /// </summary>
    private static void SetEdgeRemoval(JoltPhysicsBackend backend, FrameHandle frame, bool on)
    {
        BodyID ring = backend.BodiesIn(frame).Single(body => body.Motion == MotionType.Static).Id;
        lock (JoltRuntime.NativeGate)
        {
            BodyLockInterface locks = backend.SystemOf(frame)!.BodyLockInterface;
            locks.LockWrite(ring, out BodyLockWrite write);
            if (!write.Succeeded || write.Body is not { } body)
                throw new InvalidOperationException("The ring's body cannot be locked.");
            body.EnhancedInternalEdgeRemoval = on;
            locks.UnlockWrite(write);
        }
    }

    /// <summary>
    /// The pose of a body of half size <see cref="Half"/> that stands on the floor at the angle <paramref name="theta"/>,
    /// <paramref name="y"/> along the axis, 1 mm above the facet there, turned so that its up faces the axis.
    /// </summary>
    private static DPose OnFloor(double theta, double y)
    {
        double step = Math.Tau / Segments;
        double middle = (Math.Floor(theta / step) + 0.5) * step;
        double floor = Inner * Math.Cos(Math.PI / Segments) / Math.Cos(theta - middle);
        var radial = new Double3(Math.Cos(theta), 0, -Math.Sin(theta));
        DQuaternion turn = DQuaternion.FromAxisAngle(Double3.UnitY, theta) * DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2);
        if ((turn.Rotate(Double3.UnitY) + radial).Length > 1e-9)
            throw new InvalidOperationException("The body's up does not face the axis.");
        return new DPose((floor - Half - 0.001) * radial + new Double3(0, y, 0), turn);
    }

    /// <summary>The profile's corner (ρ, y) at the angle 2π k / n: (ρ cos θ, y, -ρ sin θ).</summary>
    private static Vector3 Point(Vector2 corner, int k)
    {
        double theta = Math.Tau * k / Segments;
        return new Vector3((float)(corner.X * Math.Cos(theta)), corner.Y, (float)(-corner.X * Math.Sin(theta)));
    }

    /// <summary>The ring's profile, counterclockwise with ρ to the right and y up, as in <c>RevolvedSolid.Ring</c>.</summary>
    private static readonly Vector2[] Profile = [new(Inner, -HalfWidth), new(Outer, -HalfWidth), new(Outer, HalfWidth), new(Inner, HalfWidth)];

    /// <summary>The demo's collision shape of the ring: one convex hull per segment, of the profile at its two angles, unrounded.</summary>
    private static CompoundShapeDesc RingHulls()
    {
        var children = new CompoundChild[Segments];
        for (int k = 0; k < Segments; k++)
            children[k] = new CompoundChild(new ConvexHullShapeDesc([.. Profile.SelectMany(c => new[] { Point(c, k), Point(c, (k + 1) % Segments) })], ConvexRadius: 0), Pose.Identity);
        return new CompoundShapeDesc(children);
    }

    /// <summary>
    /// The demo's mesh of the ring: at each angle, two vertices for each edge of the profile, and two triangles for each
    /// edge in each segment, counterclockwise seen from outside.
    /// </summary>
    private static MeshShapeDesc RingMesh()
    {
        int perAngle = 2 * Profile.Length;
        var vertices = new Vector3[Segments * perAngle];
        for (int k = 0; k < Segments; k++)
            for (int e = 0; e < Profile.Length; e++)
            {
                vertices[k * perAngle + 2 * e] = Point(Profile[e], k);
                vertices[k * perAngle + 2 * e + 1] = Point(Profile[(e + 1) % Profile.Length], k);
            }
        var indices = new List<int>();
        for (int k = 0; k < Segments; k++)
        {
            int next = (k + 1) % Segments;
            for (int e = 0; e < Profile.Length; e++)
            {
                int a = k * perAngle + 2 * e, b = a + 1, aNext = next * perAngle + 2 * e, bNext = aNext + 1;
                indices.AddRange([a, aNext, bNext, a, bNext, b]);
            }
        }
        return new MeshShapeDesc(vertices, [.. indices]);
    }

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup() => world.Dispose();

    /// <summary>
    /// ter-fb3w: <c>--edges &lt;rounds&gt;</c> sets up the four scenes and ticks each once per round, in turn, so that a
    /// change in the machine's load falls on all four alike, which BenchmarkDotNet's runs one after the other do not
    /// give. Prints the mean tick of each scene, and the change that the flag makes on each floor.
    /// </summary>
    public static void Interleaved(int rounds)
    {
        var scenes = new List<EdgeRemovalBenchmarks>();
        foreach (RingFloor floor in Enum.GetValues<RingFloor>())
            foreach (bool on in new[] { false, true })
            {
                var scene = new EdgeRemovalBenchmarks { Floor = floor, EdgeRemoval = on };
                scene.Setup();
                scenes.Add(scene);
            }
        var totals = new double[scenes.Count];
        var clock = new System.Diagnostics.Stopwatch();
        for (int round = 0; round < rounds; round++)
            for (int i = 0; i < scenes.Count; i++)
            {
                clock.Restart();
                scenes[i].Tick();
                totals[i] += clock.Elapsed.TotalMicroseconds;
            }
        for (int i = 0; i < scenes.Count; i += 2)
        {
            double off = totals[i] / rounds, on = totals[i + 1] / rounds;
            Console.WriteLine($"{scenes[i].Floor}: {off:F1} us a tick without the flag, {on:F1} us with it, {(on / off - 1) * 100:+0.0;-0.0} %");
        }
        foreach (EdgeRemovalBenchmarks scene in scenes)
            scene.Cleanup();
    }
}
