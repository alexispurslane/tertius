using BenchmarkDotNet.Attributes;
using JoltPhysicsSharp;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of forgetting the contacts of one body that leaves its system among many others (ter-9sri). 2,000 cubes of
/// 1 m stand on a static floor at 1 g, in 40 rows of 50, each face against the next, so that the system holds about 5,900
/// started pairs: 2,000 with the floor and 3,910 between neighbors. One run forgets the five pairs of a cube in the middle
/// of the grid, as the removal of a body or a ghost does, and then carries them back as held pairs, so that every run
/// starts from the same tables. Nothing ticks.
/// </summary>
[MemoryDiagnoser]
public class ContactForgetBenchmarks
{
    private const int Columns = 50;
    private const int Rows = 40;

    private readonly List<RawEnd> ended = [];
    private readonly List<BodyID> partners = [];
    private World world = null!;
    private FrameWorld system = null!;
    private BodyID body;

    [GlobalSetup]
    public void Setup()
    {
        // Room for every contact: the default limits hold 4,096 contact constraints.
        var backend = new JoltPhysicsBackend(
            new()
            {
                WorkerThreads = 2,
                MaxBodyPairsPerFrame = 65536,
                MaxContactConstraintsPerFrame = 65536,
            }
        );
        world = new(new() { MaxDegreeOfParallelism = 1, DisposePhysics = true }, backend);
        FrameHandle ground = world.CreateRootFrame(
            new(FrameBounds.Sphere(1e6f), GravityField.Uniform(new(0, -9.81f, 0)))
        );
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, -0.5, 0),
                Body = new(Shape: new BoxShapeDesc(new(60, 0.5f, 60)), Motion: BodyMotion.Static),
            }
        );
        var cube = new BodyDesc(Shape: new BoxShapeDesc(new(0.5f)), Mass: 10);
        Entity middle = default;
        for (int i = 0; i < Columns * Rows; i++)
        {
            Entity spawned = world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = At(i % Columns - Columns / 2, 0.5, i / Columns - Rows / 2),
                    Body = cube,
                }
            );
            if (i == Rows / 2 * Columns + Columns / 2)
                middle = spawned;
        }

        // Two seconds: the cubes settle.
        for (int i = 0; i < 120; i++)
            world.Tick();

        system = backend.FrameWorldOf(ground) ?? throw new InvalidOperationException("The ground has no system.");
        body = backend
            .BodiesIn(ground)
            .Single(facts =>
                BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, middle)
            )
            .Id;
        system.StartedPairsOf(body, partners);
        if (partners.Count == 0)
            throw new InvalidOperationException("The middle cube has no started pair.");
        var started = new List<RawEnd>();
        system.StartedPairs(started);
        Console.WriteLine(
            $"// The system holds {started.Count} started pairs and {system.PendingPairCount} pending ones, the middle cube {partners.Count}."
        );
    }

    private static DPose At(double x, double y, double z) => new(new(x, y, z), DQuaternion.Identity);

    [Benchmark]
    public int ForgetOneBodyAndCarryItsPairsBack()
    {
        ended.Clear();
        system.ForgetContacts(body, ended);
        foreach (BodyID other in partners)
            system.Carry(body, other);
        return ended.Count;
    }

    [GlobalCleanup]
    public void Cleanup() => world.Dispose();
}
