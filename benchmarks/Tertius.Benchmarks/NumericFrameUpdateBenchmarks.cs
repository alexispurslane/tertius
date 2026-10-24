using BenchmarkDotNet.Attributes;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of one <see cref="FrameTree.UpdateNumericFrames"/> in a space with many numeric frames and many child bubbles
/// (ter-2gg0). Space holds <see cref="Rocks"/> rocks of radius 1 m that fly at 60 m/s along X, 100 m apart along X. Each
/// rock counts as a character (<see cref="NumericBody.Character"/>), which always thrusts, so each one is above the 50 m/s
/// from which a group that thrusts falls (<see cref="NumericFrameOptions.ThrustCreateSpeed"/>), and falls in a numeric
/// frame of its own from the first update. Beside their line, 50 m away, hang <see cref="Bubbles"/> parked
/// stations, 100 m apart, each with a bubble of radius 40 m and 10 crates of radius 1 m at rest in it. The crates lie at
/// least 24 m inside their bubbles, so none of them sticks out and no station needs work: the update measures the merge
/// test of the rocks in space and the scan of the stations for places of ghosts. <see cref="NumericFramePlaceBenchmarks"/>
/// measures the places themselves. The update runs in a steady state: the bodies keep their homes, and nothing moves
/// between updates.
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
        FrameHandle space = frames.Create(
            frameOwner: new(next++, 1),
            parent: FrameHandle.Null,
            localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e9f)
        );
        var list = new List<NumericBody>();
        for (int r = 0; r < Rocks; r++)
        {
            var position = new Double3(100 * r, 0, 0);
            list.Add(
                new()
                {
                    Entity = new(next++, 1),
                    Semantic = space,
                    Home = space,
                    Position = position,
                    HomePosition = position,
                    Velocity = new(60, 0, 0),
                    Radius = 1,
                    Mass = 1,
                    Character = true,
                }
            );
        }

        for (int b = 0; b < Bubbles; b++)
        {
            FrameHandle station = frames.Create(
                frameOwner: new(next++, 1),
                parent: space,
                localPose: new(new(100 * b, 50, 0), DQuaternion.Identity),
                frameBounds: FrameBounds.Sphere(40)
            );
            for (int c = 0; c < 10; c++)
            {
                var position = new Double3(3 * c - 15, 0, 0);
                list.Add(
                    new()
                    {
                        Entity = new(next++, 1),
                        Semantic = station,
                        Home = station,
                        Position = position,
                        HomePosition = position,
                        Radius = 1,
                        Mass = 1,
                    }
                );
            }
        }

        bodies = [.. list];
        // Each move puts a body in its new home, at the same place in its semantic frame.
        applyMoves = moves =>
        {
            foreach (NumericMove move in moves)
                for (int i = 0; i < bodies.Length; i++)
                    if (bodies[i].Entity == move.Entity)
                        bodies[i] = bodies[i] with
                        {
                            Home = move.To,
                            HomePosition = bodies[i].Position - frames.HomeOffset(move.To, bodies[i].Semantic),
                        };
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
        frames.UpdateNumericFrames(
            bodies: bodies,
            dt: 1.0 / 60,
            step: 1.0 / 60,
            changes: changes,
            applyMoves: applyMoves
        );
    }
}

/// <summary>
/// The cost of the places of ghosts in one <see cref="FrameTree.UpdateNumericFrames"/> (ter-4psc): the scan of the bodies of
/// a semantic frame's parent and siblings for each semantic frame whose update runs. Space holds
/// <see cref="ParentBodies"/>
/// rocks of radius 1 m on a grid 40 m apart that fills a box around a row of 10 parked ships. Each rock drifts at 5 m/s in
/// one of six directions, so a rock near a ship can touch its bubble before the next update, and no rock needs a numeric
/// frame. The ships hang 80 m apart along X, so their bubbles of radius 40 m touch. Each ship has 6 crates of radius 1 m
/// at rest 39.5 m from its center along its axes, which stick out of its bubble, and the crates along X stick into the
/// bubbles of its neighbors. A character in each ship flies at 60 m/s, above the speed from which a group that thrusts
/// falls, so the update of every ship runs. The merge test links the character to the places of the crates of the
/// neighbors, which keep its group in its ship's own system. So each update scans
/// every rock 10 times for places in the ships, and the crates of each ship 9 times for places in its siblings. Space needs
/// no work. The update runs in a steady state: the bodies keep their homes, and nothing moves between updates.
/// </summary>
[MemoryDiagnoser]
public class NumericFramePlaceBenchmarks
{
    private const int Ships = 10;

    private static readonly Double3[] Axes =
    [
        new(1, 0, 0),
        new(-1, 0, 0),
        new(0, 1, 0),
        new(0, -1, 0),
        new(0, 0, 1),
        new(0, 0, -1),
    ];

    [Params(1_000, 10_000)]
    public int ParentBodies;

    private readonly FrameTree frames = new();
    private readonly PhysicsChanges changes = new();
    private NumericBody[] bodies = [];
    private Action<List<NumericMove>> applyMoves = null!;

    [GlobalSetup]
    public void Setup()
    {
        uint next = 1;
        FrameHandle space = frames.Create(
            frameOwner: new(next++, 1),
            parent: FrameHandle.Null,
            localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e9f)
        );
        var list = new List<NumericBody>();
        var ships = new Double3[Ships];
        for (int s = 0; s < Ships; s++)
        {
            ships[s] = new(80 * s, 0, 0);
            FrameHandle ship = frames.Create(
                frameOwner: new(next++, 1),
                parent: space,
                localPose: new(ships[s], DQuaternion.Identity),
                frameBounds: FrameBounds.Sphere(40)
            );
            list.Add(
                Body(id: next++, frame: ship, position: Double3.Zero, velocity: new(60, 0, 0)) with
                {
                    Character = true,
                }
            );
            foreach (Double3 axis in Axes)
                list.Add(Body(id: next++, frame: ship, position: axis * 39.5, velocity: Double3.Zero));
        }

        // A grid 40 m apart around the row of ships, outside every bubble, as many points as asked: 2 * Ships + 8 columns
        // along X, from 160 m before the first ship to 120 m after the last, and as many rows along Y and Z as the count needs.
        int columns = 2 * Ships + 8;
        int side = (int)Math.Ceiling((Math.Sqrt(ParentBodies / (double)columns) - 1) / 2);
        int rocks = 0;
        for (int x = -4; rocks < ParentBodies; x++)
        for (int y = -side; y <= side && rocks < ParentBodies; y++)
        for (int z = -side; z <= side && rocks < ParentBodies; z++)
        {
            var position = new Double3(40 * x, 40 * y + 20, 40 * z + 20);
            bool inBubble = false;
            foreach (Double3 ship in ships)
                inBubble |= (position - ship).Length < 42;
            if (inBubble)
                continue;
            list.Add(Body(id: next++, frame: space, position: position, velocity: Axes[rocks % Axes.Length] * 5));
            rocks++;
        }

        bodies = [.. list];
        int moved = 0;
        applyMoves = moves => moved += moves.Count;
        Update();
        if (moved != 0 || frames.NumericCount != 0)
            throw new InvalidOperationException(
                $"Expected a steady state without numeric frames, found {moved} moves and {frames.NumericCount} frames."
            );
        Console.WriteLine($"// {rocks} rocks.");
    }

    private static NumericBody Body(uint id, FrameHandle frame, Double3 position, Double3 velocity) =>
        new()
        {
            Entity = new(id, 1),
            Semantic = frame,
            Home = frame,
            Position = position,
            HomePosition = position,
            Velocity = velocity,
            Radius = 1,
            Mass = 1,
        };

    [Benchmark]
    public void Update()
    {
        changes.Clear();
        frames.UpdateNumericFrames(
            bodies: bodies,
            dt: 1.0 / 60,
            step: 1.0 / 60,
            changes: changes,
            applyMoves: applyMoves
        );
    }
}
