using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Step 8 of the sync point copies the rows that gameplay wrote on the world's workers once there are at least
/// <see cref="EntityTable.ParallelCopyThreshold"/> of them. A world on four workers with that threshold at its lowest,
/// one row, copies on the workers in every tick in which gameplay wrote a row, and must end in the world of a run on one
/// worker, which copies on the ticking thread. The entities move, change their render rows and their components, spawn,
/// die, and read their neighbors from the spatial grid, which takes the moved entities in the order of the copy, so a
/// change of that order or of any copied row shows in the hash.
/// </summary>
public sealed class ParallelRowCopyWorldTests
{
    /// <summary>A seeded entity that writes every row that gameplay can write, and folds what it reads into its hash.</summary>
    private sealed class Wanderer : IComponent
    {
        private static readonly EventMask WantsMask = EventMask.Of<Tick>();
        private readonly List<Entity> neighbors = [];
        private Metal? metal;

        public ulong Rng;
        public ulong Hash;

        public int Priority => 0;
        public EventMask Wants => WantsMask;

        private double Next()
        {
            // xorshift64*: deterministic and seeded per entity.
            Rng ^= Rng >> 12;
            Rng ^= Rng << 25;
            Rng ^= Rng >> 27;
            return ((Rng * 0x2545F4914F6CDD1DUL) >> 11) * (1.0 / (1UL << 53));
        }

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (!events.Of<Tick>().Any())
                return;
            DPose pose = self.PoseInOwnFrame();
            self.SetTransform(pose with { Position = pose.Position + new Double3(Next() - 0.5, 0, Next() - 0.5) });
            if (Next() < 0.5)
                self.Render.Scale = new((float)Next());
            if (Next() < 0.1)
            {
                if (metal is null)
                {
                    self.AddComponent(metal = new());
                }
                else
                {
                    self.RemoveComponent(metal);
                    metal = null;
                }
            }

            self.Snapshot.QueryRadius(frame: self.Frame, center: pose.Position, radius: 6, results: neighbors);
            foreach (Entity neighbor in neighbors)
            {
                Hash = WorldHash.Mix(Hash, neighbor.Raw);
                Hash = WorldHash.Mix(Hash, self.Snapshot.RenderOf(neighbor).Scale);
            }

            if (Next() < 0.01)
                self.Spawn(new()
                {
                    Frame = self.Frame,
                    Pose = pose,
                    Components = [new Wanderer { Rng = Rng | 1 }]
                });
            if (Next() < 0.01)
                self.Destroy();
        }
    }

    private static ulong Run(int maxDop)
    {
        using var world = new World(new()
        {
            MaxDegreeOfParallelism = maxDop,
            ChunkSize = 8,
            SpawnPoolSize = 8,
            DebugChecks = true
        });
        world.Entities.ParallelCopyThreshold = 1;
        FrameHandle root = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        var random = new Random(20261018);
        for (int i = 0; i < 120; i++)
            world.Spawn(new()
            {
                Frame = root,
                Pose = TestWorld.At(random.NextDouble() * 40 - 20, 0, random.NextDouble() * 40 - 20),
                Render = new RenderData { Mesh = new(i % 3), Flags = RenderFlags.Visible },
                Components = [new Wanderer { Rng = (ulong)random.NextInt64() | 1 }]
            });
        for (int tick = 0; tick < 60; tick++)
            world.Tick();
        return WorldHash.Of(world, static (soFar, component) => component switch
        {
            Wanderer w => WorldHash.Mix(WorldHash.Mix(soFar, w.Hash), w.Rng),
            Metal => WorldHash.Mix(soFar, 7),
            _ => soFar
        });
    }

    [Fact]
    public void AWorldThatCopiesRowsOnTheWorkersEndsInTheWorldOfOneThatCopiesOnOneThread()
    {
        ulong oneWorker = Run(maxDop: 1);
        ulong fourWorkers = Run(maxDop: 4);

        fourWorkers.Should().Be(oneWorker);
    }
}
