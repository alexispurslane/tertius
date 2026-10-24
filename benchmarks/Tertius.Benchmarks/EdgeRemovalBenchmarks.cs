using System.Diagnostics;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using JoltPhysicsSharp;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of Jolt's enhanced internal edge removal on the floor of a ring (ter-fb3w). One tick of a world in which
/// 200 boxes and 200 balls, or 400 balls, rest and slide, or 100 characters walk, on the floor of a static ring, in
/// uniform gravity of 1 g along +X, so the floor's lowest line runs along the ring's axis at the angle 0. The ring is
/// the demo's <c>RevolvedSolid.Ring(20, 20.5, 5, 256)</c>, built here the same way, because this project does not
/// reference the demo: 256 segments of 20 m to 20.5 m from the axis and 10 m along it, so each facet of the floor is
/// 0.49 m wide, and a body that slides 1 m crosses two joints.
/// <list type="bullet">
/// <item><see cref="Floor"/> is the ring's collision shape: <see cref="RingFloor.Hulls"/>, the demo's compound of one
/// convex hull of convex radius 0 per segment, or <see cref="RingFloor.Mesh"/>, Jolt's mesh shape of the demo's mesh of
/// the ring. Both are made of sharp pieces, so physics sets the flag on the ring's body.</item>
/// <item><see cref="Load"/> is what stands on the floor (see <see cref="FloorLoad"/>).</item>
/// <item><see cref="EdgeRemoval"/> sets Jolt's <c>EnhancedInternalEdgeRemoval</c>, or clears it, after the backend has
/// created the bodies, where it acts on the load. Under balls and boxes, it is the ring's body's flag: Jolt uses the
/// flag of either body of a pair, the balls and boxes are convex, and the static ring is the second body of each of its
/// pairs, whose internal edges Jolt removes, so the ring's flag decides for every pair. Under characters, it is each
/// character's flag: a Jolt character finds its contacts by a query of its own, which reads only its own flag.</item>
/// </list>
/// The balls and boxes stand in 20 lanes along the floor, 0.5 m apart along the axis, each lane all boxes of 0.3 m and
/// 10 kg or all balls of 0.3 m across and 1 kg: every other lane of boxes under <see cref="FloorLoad.BallsAndBoxes"/>,
/// and no lane under <see cref="FloorLoad.Balls"/>. In half of the lanes the bodies slide: each is 0.45 m from the
/// next, and a <see cref="Shuttle"/> holds the speed of each along the floor at 1 m/s and turns it every second, so a
/// lane moves together and crosses two joints each way. In the other half they rest: boxes 0.45 m apart, which friction
/// holds on the floor up to 12° from its lowest line, and balls in a touching row about the lowest line, which each
/// other hold. They fall asleep, so 200 bodies are awake. The characters walk in 10 lanes
/// (see <see cref="SpawnCharacters"/> and <see cref="Walker"/>), and all of them are awake.
/// <para>
/// A scene measures what the flag costs only where the bodies move alike with and without it. <see cref="Setup"/>
/// prints how many bodies are awake after 2 s, how far a sliding ball rises above the floor, and how far a sliding box
/// tilts, and <see cref="Interleaved"/> prints how many are awake after its rounds. The flag changes how a sliding ball
/// crosses a joint: without it, the ball rises up to 0.92 mm on the hulls and 0.95 mm on the mesh, against 0.26 mm and
/// 0.27 mm with it. In two scenes it changes much more, and those measure the motion, not the flag. On the hulls
/// without the flag, the sliding boxes trip at the joints and tumble, up to 180° against 1.42° with it, and they wake
/// the resting lanes: after 5000 rounds 333 bodies are awake, against 200 with the flag. On the mesh without the flag,
/// the 400 balls wake each other: 399 are awake after the rounds, against 200. In the other scenes, 200 bodies stay
/// awake both ways, or 204 against 200 under balls and boxes on the mesh, and every character stays on the floor.
/// </para>
/// <para>
/// Measured on 2026-10-19 in Release, on a machine that other work loaded to about 4 of its 10 cores, with the twelve
/// scenes ticked in turn (<c>--edges 5000</c>, see <see cref="Interleaved"/>), three runs. In the scenes whose motion
/// the flag does not change: balls on the hulls, -1.8 %, -2.4 % and -2.2 % (about 345 µs a tick); balls and boxes on
/// the mesh, -4.8 %, -4.8 % and -5.1 % (about 410 µs); characters on the hulls, +1.8 %, +2.2 % and +2.4 % (about
/// 565 µs), where the character's flag drops the contacts at the joints; characters on the mesh, -4.7 %, -4.5 % and
/// -4.6 % (about 715 µs). So the floor's flag costs nothing that these runs can see on either floor, and the character's flag
/// costs about 2 % on the hulls and nothing on the mesh. In the two scenes whose motion it changes, balls and boxes on
/// the hulls (-24.5 %, -23.3 % and -24.5 %) and balls on the mesh (-28.5 %, -31.3 % and -29.6 %), the tick without the
/// flag is longer because more bodies are awake, so those numbers say nothing about what the flag costs.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class EdgeRemovalBenchmarks
{
    private const int Segments = 256;
    private const float Inner = 20, Outer = 20.5f, HalfWidth = 5;
    private const int Lanes = 20, PerLane = 20;
    private const float Half = 0.15f;
    private const int CharacterLanes = 10, CharactersPerLane = 10;
    private const float PersonHalfHeight = 0.6f, PersonRadius = 0.3f, WalkSpeed = 3;

    /// <summary>The ring's profile, counterclockwise with ρ to the right and y up, as in <c>RevolvedSolid.Ring</c>.</summary>
    private static readonly Vector2[] Profile =
        [new(Inner, -HalfWidth), new(Outer, -HalfWidth), new(Outer, HalfWidth), new(Inner, HalfWidth)];

    [Params(RingFloor.Hulls, RingFloor.Mesh)]
    public RingFloor Floor;

    [Params(FloorLoad.BallsAndBoxes, FloorLoad.Balls, FloorLoad.Characters)]
    public FloorLoad Load;

    [Params(false, true)] public bool EdgeRemoval;

    private World world = null!;
    private JoltPhysicsBackend backend = null!;
    private FrameHandle frame;
    private List<Entity> people = [];

    public enum RingFloor
    {
        Hulls,
        Mesh
    }

    /// <summary>What stands on the floor: 200 boxes and 200 balls, 400 balls, or 100 characters that walk.</summary>
    public enum FloorLoad
    {
        BallsAndBoxes,
        Balls,
        Characters
    }

    /// <summary>
    /// Holds the speed of its body along the ring's floor, about the ring's axis, at 1 m/s toward growing angle for 60
    /// ticks and then the other way for 60, and leaves the rest of its velocity to Jolt.
    /// </summary>
    private sealed class Shuttle : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

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

    /// <summary>
    /// Walks its character at <see cref="WalkSpeed"/> across its up, toward growing angle for half a second, then the other
    /// way for a second, and so on, so that a lane walks 1.5 m to each side of where it started, across three joints.
    /// </summary>
    private sealed class Walker : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            // Gravity is along +X, so the walk across the up runs along -Z near the angle 0, toward growing angle.
            float speed = (self.Tick + 30) / 60 % 2 == 0 ? WalkSpeed : -WalkSpeed;
            self.Body.MoveCharacter(new(0, 0, -speed));
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        backend = new(new() { WorkerThreads = 2 });
        world = new(new() { MaxDegreeOfParallelism = 1, DisposePhysics = true }, backend);
        frame = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f), GravityField.Uniform(new(9.81f, 0, 0))));
        world.Spawn(new()
        {
            Frame = frame,
            Body = new(Shape: Floor == RingFloor.Hulls ? RingHulls() : RingMesh(), Motion: BodyMotion.Static)
        });
        List<Entity> slidingBoxes = [], slidingBalls = [];
        if (Load == FloorLoad.Characters)
            people = SpawnCharacters();
        else
            (slidingBoxes, slidingBalls) = SpawnBallsAndBoxes(withBoxes: Load == FloorLoad.BallsAndBoxes);
        // The first tick creates the bodies.
        world.Tick();
        if (Load == FloorLoad.Characters)
            SetCharacterEdgeRemoval(backend, people, EdgeRemoval);
        else
            SetEdgeRemoval(backend, frame, EdgeRemoval);
        // Two seconds: the resting boxes fall asleep, and the sliding lanes and the walkers have turned twice.
        double tilt = 0, lift = 0;
        for (int i = 0; i < 120; i++)
        {
            world.Tick();
            foreach (Entity box in slidingBoxes)
                tilt = Math.Max(tilt, TiltFromFloor(world.PoseIn(box, frame)));
            foreach (Entity ball in slidingBalls)
                lift = Math.Max(lift, LiftFromFloor(world.PoseIn(ball, frame)));
        }

        string motion = Load == FloorLoad.Characters
            ? ""
            : $", a sliding ball rises up to {lift * 1000:F2} mm" + (slidingBoxes.Count > 0
                ? $", a sliding box tilts up to {tilt * 180 / Math.PI:F2}°"
                : "");
        Console.WriteLine($"// {Floor}, {Load}, edge removal {EdgeRemoval}, after 2 s: {State()}{motion}.");
    }

    /// <summary>How many of the balls and boxes are awake, or how many of the characters are on the floor.</summary>
    private string State() => Load == FloorLoad.Characters
        ? $"{people.Count(person => world.PoseIn(person, frame).Position.X > Inner * 0.9)} of {people.Count} characters on the floor"
        : $"{backend.BodiesIn(frame).Count(body => body.Active && body.Motion == MotionType.Dynamic)} of {Lanes * PerLane} bodies awake";

    /// <summary>
    /// The angle, in radians, between the up of a body at <paramref name="pose"/>, its local +Y, and the direction from
    /// its center to the ring's axis. A box flat on a facet tilts up to half the facet's turn, π / 256 (0.70°).
    /// </summary>
    private static double TiltFromFloor(DPose pose)
    {
        Double3 p = pose.Position, up = pose.Rotation.Rotate(Double3.UnitY);
        Double3 inward = new Double3(-p.X, 0, -p.Z).Normalized();
        return Math.Acos(Math.Clamp(Double3.Dot(up, inward), -1, 1));
    }

    /// <summary>
    /// How far, in meters, a ball of radius <see cref="Half"/> at <paramref name="pose"/> is above the facet under its
    /// center, toward the axis: 0 for a ball that rests on the facet.
    /// </summary>
    private static double LiftFromFloor(DPose pose)
    {
        Double3 p = pose.Position;
        double step = Math.Tau / Segments, middle = (Math.Floor(Math.Atan2(-p.Z, p.X) / step) + 0.5) * step;
        double apothem = Inner * Math.Cos(Math.PI / Segments);
        return apothem - (p.X * Math.Cos(middle) - p.Z * Math.Sin(middle)) - Half;
    }

    /// <summary>
    /// Spawns the 20 lanes, every other lane of boxes when <paramref name="withBoxes"/> is true and of balls otherwise,
    /// and returns the boxes and the balls that slide.
    /// </summary>
    private (List<Entity> SlidingBoxes, List<Entity> SlidingBalls) SpawnBallsAndBoxes(bool withBoxes)
    {
        List<Entity> slidingBoxes = [], slidingBalls = [];
        var box = new BoxShapeDesc(new(Half));
        var ball = new SphereShapeDesc(Half);
        for (int lane = 0; lane < Lanes; lane++)
        {
            bool boxes = withBoxes && lane % 2 == 0, slides = lane / 2 % 2 == 0;
            double y = (lane - (Lanes - 1) / 2.0) * 0.5;
            // A row of resting balls touches, so that each holds the next on the curved floor.
            double spacing = !slides && !boxes ? 2 * Half + 0.001 : 0.45;
            for (int i = 0; i < PerLane; i++)
            {
                double theta = (i - (PerLane - 1) / 2.0) * spacing / Inner;
                Entity body = world.Spawn(new()
                {
                    Frame = frame,
                    Pose = OnFloor(theta, y),
                    Body = new(Shape: boxes ? box : ball, Mass: boxes ? 10 : 1),
                    Components = slides ? [new Shuttle()] : []
                });
                if (slides)
                    (boxes ? slidingBoxes : slidingBalls).Add(body);
            }
        }

        return (slidingBoxes, slidingBalls);
    }

    /// <summary>
    /// 10 lanes of 10 characters, a capsule of half height 0.6 m and radius 0.3 m and 70 kg each, as the demo's person:
    /// the lanes 0.9 m apart along the axis, and the characters of a lane 1 m apart along the floor, so that they stand
    /// up to 13° and walk up to 17° from its lowest line. Each stands upright against the gravity, 1 cm above the facet
    /// under it, and walks with a <see cref="Walker"/>, so a lane walks together and its characters never meet.
    /// </summary>
    private List<Entity> SpawnCharacters()
    {
        var capsule = new CapsuleShapeDesc(PersonHalfHeight, PersonRadius);
        // The character's up, its local +Y, is -X, against the gravity.
        DQuaternion upright = DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2);
        double step = Math.Tau / Segments;
        var people = new List<Entity>();
        for (int lane = 0; lane < CharacterLanes; lane++)
        for (int i = 0; i < CharactersPerLane; i++)
        {
            double theta = (i - (CharactersPerLane - 1) / 2.0) / Inner;
            double middle = (Math.Floor(theta / step) + 0.5) * step;
            var inward = new Double3(-Math.Cos(middle), 0, Math.Sin(middle));
            double floor = Inner * Math.Cos(Math.PI / Segments) / Math.Cos(theta - middle);
            var floorPoint = new Double3(floor * Math.Cos(theta), (lane - (CharacterLanes - 1) / 2.0) * 0.9,
                -floor * Math.Sin(theta));
            Double3 lowerCenter = floorPoint + (PersonRadius + 0.01) * inward;
            people.Add(world.Spawn(new()
            {
                Frame = frame,
                Pose = new(lowerCenter - PersonHalfHeight * Double3.UnitX, upright),
                Body = new(Shape: capsule, Motion: BodyMotion.Character, Mass: 70),
                Components = [new Walker()]
            }));
        }

        return people;
    }

    /// <summary>Sets or clears <c>EnhancedInternalEdgeRemoval</c> on the Jolt character of each of <paramref name="people"/>.</summary>
    private static void SetCharacterEdgeRemoval(JoltPhysicsBackend backend, List<Entity> people, bool on)
    {
        lock (backend.Gate)
        {
            foreach (Entity person in people)
                (backend.CharacterOf(person) ??
                 throw new InvalidOperationException("A person is not a walking character."))
                    .EnhancedInternalEdgeRemoval = on;
        }
    }

    /// <summary>
    /// Sets or clears <c>EnhancedInternalEdgeRemoval</c> on the ring's body, the one static body in
    /// <paramref name="frame"/>'s system.
    /// </summary>
    private static void SetEdgeRemoval(JoltPhysicsBackend backend, FrameHandle frame, bool on)
    {
        BodyID ring = backend.BodiesIn(frame).Single(body => body.Motion == MotionType.Static).Id;
        lock (backend.Gate)
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
        DQuaternion turn = DQuaternion.FromAxisAngle(Double3.UnitY, theta) *
                           DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2);
        return (turn.Rotate(Double3.UnitY) + radial).Length > 1e-9
            ? throw new InvalidOperationException("The body's up does not face the axis.")
            : new((floor - Half - 0.001) * radial + new Double3(0, y, 0), turn);
    }

    /// <summary>The profile's corner (ρ, y) at the angle 2π k / n: (ρ cos θ, y, -ρ sin θ).</summary>
    private static Vector3 Point(Vector2 corner, int k)
    {
        double theta = Math.Tau * k / Segments;
        return new((float)(corner.X * Math.Cos(theta)), corner.Y, (float)(-corner.X * Math.Sin(theta)));
    }

    /// <summary>The demo's collision shape of the ring: one convex hull per segment, of the profile at its two angles, unrounded.</summary>
    private static CompoundShapeDesc RingHulls()
    {
        var children = new CompoundChild[Segments];
        for (int k = 0; k < Segments; k++)
            children[k] =
                new(
                    new ConvexHullShapeDesc(
                        [.. Profile.SelectMany(c => new[] { Point(c, k), Point(c, (k + 1) % Segments) })],
                        ConvexRadius: 0), Pose.Identity);
        return new(children);
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

        return new(vertices, [.. indices]);
    }

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup() => world.Dispose();

    /// <summary>
    /// ter-fb3w: <c>--edges &lt;rounds&gt;</c> sets up the twelve scenes and ticks each once per round, in turn, so
    /// that a change in the machine's load falls on all of them alike, which BenchmarkDotNet's runs one after the other
    /// do not give. Prints the mean tick of each scene, the change that the flag makes on each floor under each load,
    /// and then how many bodies of each scene are awake, or how many of its characters are on the floor.
    /// </summary>
    public static void Interleaved(int rounds)
    {
        var scenes = new List<EdgeRemovalBenchmarks>();
        foreach (FloorLoad load in Enum.GetValues<FloorLoad>())
        foreach (RingFloor floor in Enum.GetValues<RingFloor>())
        foreach (bool on in new[] { false, true })
        {
            var scene = new EdgeRemovalBenchmarks { Floor = floor, Load = load, EdgeRemoval = on };
            scene.Setup();
            scenes.Add(scene);
        }

        double[] totals = new double[scenes.Count];
        var clock = new Stopwatch();
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
            Console.WriteLine(
                $"{scenes[i].Load} on the {scenes[i].Floor}: {off:F1} us a tick without the flag, {on:F1} us with it, {(on / off - 1) * 100:+0.0;-0.0} %");
        }

        foreach (EdgeRemovalBenchmarks scene in scenes)
        {
            Console.WriteLine(
                $"// {scene.Floor}, {scene.Load}, edge removal {scene.EdgeRemoval}, after the rounds: {scene.State()}.");
            scene.Cleanup();
        }
    }
}
