using BenchmarkDotNet.Attributes;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of one <see cref="FrameTree.UpdateNumericFrames"/> in a space with many numeric frames and many child bubbles
/// (ter-2gg0). Space holds <see cref="Rocks"/> rocks of radius 1 m that fly at 60 m/s along X, 100 m apart along X. Each
/// rock counts as a character (<see cref="NumericBody.Character"/>), which always thrusts, so each one is above the 50 m/s
/// from which a group that thrusts falls (<see cref="NumericFrameOptions.ThrustCreateSpeed"/>), and falls in a numeric
/// frame of its own from the first update. Beside their line, 50 m away, hang <see cref="Bubbles"/> parked
/// stations, 100 m apart, each with a bubble of radius 40 m and 10 crates of radius 1 m at rest in it. Each rock may touch
/// a body that sticks out of the three stations nearest it, so the update names its frame for those three. The update
/// runs in a steady state: the bodies keep their homes, and nothing moves between updates.
/// </summary>
[MemoryDiagnoser]
public class NumericFrameUpdateBenchmarks
{
    [Params(10, 100)]
    public int Rocks;

    [Params(10, 100)]
    public int Bubbles;

    private readonly FrameTree frames = new();
    private readonly PhysicsChanges changes = new();
    private NumericBody[] bodies = [];
    private Action<List<NumericMove>> applyMoves = null!;

    [GlobalSetup]
    public void Setup()
    {
        uint next = 1;
        FrameHandle space = frames.Create(new Entity(next++, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static);
        var list = new List<NumericBody>();
        for (int r = 0; r < Rocks; r++)
        {
            var position = new Double3(100 * r, 0, 0);
            list.Add(new NumericBody
            {
                Entity = new Entity(next++, 1),
                Semantic = space,
                Home = space,
                Position = position,
                HomePosition = position,
                Velocity = new Double3(60, 0, 0),
                Radius = 1,
                Mass = 1,
                Character = true,
            });
        }
        for (int b = 0; b < Bubbles; b++)
        {
            FrameHandle station = frames.Create(new Entity(next++, 1), space, new DPose(new Double3(100 * b, 50, 0), DQuaternion.Identity),
                FrameBounds.Sphere(40), MotionDriver.Static);
            for (int c = 0; c < 10; c++)
            {
                var position = new Double3(3 * c - 15, 0, 0);
                list.Add(new NumericBody
                {
                    Entity = new Entity(next++, 1),
                    Semantic = station,
                    Home = station,
                    Position = position,
                    HomePosition = position,
                    Radius = 1,
                    Mass = 1,
                });
            }
        }
        bodies = list.ToArray();
        // Each move puts a body in its new home, at the same place in its semantic frame.
        applyMoves = moves =>
        {
            foreach (NumericMove move in moves)
                for (int i = 0; i < bodies.Length; i++)
                    if (bodies[i].Entity == move.Entity)
                        bodies[i] = bodies[i] with { Home = move.To, HomePosition = bodies[i].Position - frames.HomeOffset(move.To, bodies[i].Semantic) };
        };
        Update();
        Update();
        if (frames.NumericCount != Rocks)
            throw new InvalidOperationException($"Expected {Rocks} falling frames, found {frames.NumericCount}.");
        Console.WriteLine($"// {frames.NumericCount} numeric frames.");
    }

    [Benchmark]
    public void Update()
    {
        changes.Clear();
        frames.UpdateNumericFrames(bodies, 1.0 / 60, changes, applyMoves);
    }
}
