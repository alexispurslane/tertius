using System.Diagnostics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The number of Jolt worker threads of a backend (<see cref="JoltBackendOptions.WorkerThreads"/>). A default backend runs
/// one worker per processor that the runtime reports, minus one, so that <c>DOTNET_PROCESSOR_COUNT</c> bounds the native
/// workers of a test host as it bounds its xunit threads. An explicit count holds. The step ends in the same bits with any
/// count, so the count changes the speed of a step and never its result.
/// </summary>
public sealed class WorkerThreadsTests
{
    [Fact]
    public void ADefaultBackendRunsOneWorkerPerProcessorMinusOne() =>
        new JoltBackendOptions().WorkerThreadsFor(processorCount: 4).Should().Be(3);

    [Fact]
    public void ADefaultBackendOnOneProcessorRunsOneWorker() =>
        new JoltBackendOptions()
            .WorkerThreadsFor(processorCount: 1)
            .Should()
            .Be(1, "a job system without workers would run every job on the physics thread");

    [Fact]
    public void AnExplicitWorkerCountHoldsWhateverTheProcessorCount() =>
        new JoltBackendOptions { WorkerThreads = 6 }
            .WorkerThreadsFor(processorCount: 4)
            .Should()
            .Be(6);

    /// <summary>
    /// The evidence that a change of the default count changes no result: Jolt's documentation makes its determinism
    /// depend on the order of the API calls and on the binary, not on the job system, and its own determinism tests compare
    /// 0 workers with 15. The default backend runs 9 workers on a machine of 10 cores, and 3 under
    /// <c>DOTNET_PROCESSOR_COUNT=4</c>, so the test never starts more workers than the host may use.
    /// <see cref="HeapIsland"/> shows that Jolt splits this heap's island into batches for the workers.
    /// </summary>
    [Fact]
    public void AHeapEndsInTheSameBitsWithOneTwoOrTheDefaultNumberOfWorkers()
    {
        ulong one = HeapHash(new() { WorkerThreads = 1 });

        HeapHash(new() { WorkerThreads = 2 }).Should().Be(one);
        HeapHash(new()).Should().Be(one, "the default count is the runtime's processor count minus one");
    }

    private static ulong HeapHash(JoltBackendOptions options)
    {
        using World world = Heap(new JoltPhysicsBackend(options), out _);
        return WorldHash.Of(world);
    }

    /// <summary>The island of the heap (tests/STYLE.md, "Long tests").</summary>
    public sealed class HeapIsland
    {
        /// <summary>
        /// The premise of the determinism test: Jolt's large island splitter hands batches of an island's constraints to
        /// the workers only when the island holds more than <c>LargeIslandSplitter::cLargeIslandTreshold</c>, 128
        /// constraints (Jolt/Physics/LargeIslandSplitter.h). This test counts the contacts that touch the largest group
        /// of connected cubes at the end of a step in which every cube is awake. Each such contact is one of Jolt's contact
        /// constraints. Jolt also holds a constraint for a pair within its speculative contact distance that does not
        /// touch yet, so the count is a lower bound on the island's constraints.
        /// </summary>
        [Fact]
        public void TheAwakeHeapIsOneIslandOfMoreContactsThanJoltSplitsForItsWorkers()
        {
            var faults = new FaultInjectingBackend(new JoltPhysicsBackend(new() { WorkerThreads = 1 }));
            // The world disposes the backend, and the outputs that the wrapper recorded stay.
            Heap(faults, out Entity floor).Dispose();

            var touching = new HashSet<(Entity A, Entity B)>();
            int largest = 0;
            foreach (PhysicsStepOutput output in faults.Outputs)
            {
                foreach (ContactStarted contact in output.Contacts)
                    touching.Add((contact.A, contact.B));
                foreach (ContactEnded ended in output.ContactsEnded)
                    touching.Remove((ended.A, ended.B));
                if (output.Bodies.Count == HeapCubes)
                    largest = Math.Max(largest, LargestIslandContacts(touching, floor));
            }

            largest.Should().BeGreaterThan(128);
        }

        // The contacts of the largest group of cubes that touch each other, directly or through other cubes. The floor is
        // static, so it joins no two cubes into one island, but its contacts with the group's cubes count.
        private static int LargestIslandContacts(HashSet<(Entity A, Entity B)> touching, Entity floor)
        {
            var parent = new Dictionary<Entity, Entity>();
            foreach ((Entity a, Entity b) in touching)
                if (a != floor && b != floor)
                    parent[Root(parent, a)] = Root(parent, b);

            var contacts = new Dictionary<Entity, int>();
            foreach ((Entity a, Entity b) in touching)
            {
                Entity root = Root(parent, a == floor ? b : a);
                contacts[root] = contacts.GetValueOrDefault(root) + 1;
            }
            return contacts.Count == 0 ? 0 : contacts.Values.Max();
        }

        private static Entity Root(Dictionary<Entity, Entity> parent, Entity cube)
        {
            while (parent.TryGetValue(cube, out Entity up) && up != cube)
                cube = up;
            return cube;
        }
    }

    /// <summary>The number of cubes in the heap: layers of 6 by 6, 5 by 5 and so on up to 1 by 1.</summary>
    private const int HeapCubes = 91;

    /// <summary>
    /// A world after three seconds on <paramref name="physics"/>: a stepped pyramid of <see cref="HeapCubes"/> cubes of
    /// 1 m, 2 cm apart in each layer. Each layer sits half a cube in from the layer below, so each of its cubes lands on
    /// the corners of four cubes below. The cubes are turned a little and dropped from uneven heights.
    /// </summary>
    private static World Heap(IPhysicsBackend physics, out Entity floor)
    {
        var world = new World(
            new()
            {
                MaxDegreeOfParallelism = 1,
                InitialCapacity = 128,
                DebugChecks = false,
                DisposePhysics = true,
            },
            physics
        );
        FrameHandle ground = Ground(world, 9.81f);
        floor = Floor(world, ground);
        int i = 0;
        for (int layer = 0; layer < 6; layer++)
        for (int x = 0; x < 6 - layer; x++)
        for (int z = 0; z < 6 - layer; z++, i++)
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = new(
                        new((x + layer * 0.5) * 1.02, 0.6 + layer * 1.1 + i % 7 * 0.01, (z + layer * 0.5) * 1.02),
                        DQuaternion.FromAxisAngle(Double3.UnitY, i % 5 * 0.005)
                    ),
                    Body = Cube(),
                }
            );
        for (int tick = 0; tick < 180; tick++)
            world.Tick();
        return world;
    }
}

/// <summary>
/// Runs the tests of <see cref="JoltThreadCountTests"/> alone, because they count the threads of the whole process.
/// </summary>
[CollectionDefinition(nameof(JoltThreadCountTests), DisableParallelization = true)]
public sealed class JoltThreadCountCollection;

/// <summary>
/// The number of native threads that a backend's Jolt job system starts. A backend passes its count to Jolt, so that Jolt
/// never sizes its workers from the hardware. Each test counts the threads of the process before and after it builds a
/// backend, and allows one more thread for the runtime, which can start a thread of its own in the meantime.
/// </summary>
[Collection(nameof(JoltThreadCountTests))]
public sealed class JoltThreadCountTests
{
    [Fact]
    public void ABackendWithAnExplicitWorkerCountStartsThatManyJoltThreads() =>
        ThreadsStartedBy(new() { WorkerThreads = 2 }).Should().BeInRange(2, 3);

    [Fact]
    public void ADefaultBackendStartsOneJoltThreadPerProcessorOfTheRuntimeMinusOne()
    {
        int expected = new JoltBackendOptions().WorkerThreadsFor(Environment.ProcessorCount);

        ThreadsStartedBy(new())
            .Should()
            .BeInRange(
                expected,
                expected + 1,
                "DOTNET_PROCESSOR_COUNT sets Environment.ProcessorCount, and the hardware does not"
            );
    }

    // The threads of the process that a backend with these options adds while it lives. The first backend of the process
    // also initializes Jolt, so a backend built and disposed first keeps that out of the count.
    private static int ThreadsStartedBy(JoltBackendOptions options)
    {
        new JoltPhysicsBackend(new() { WorkerThreads = 1 }).Dispose();
        int before = ProcessThreads();
        using var backend = new JoltPhysicsBackend(options);
        return ProcessThreads() - before;
    }

    private static int ProcessThreads()
    {
        using var process = Process.GetCurrentProcess();
        return process.Threads.Count;
    }
}
