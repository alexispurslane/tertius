using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    public void ANegativeWorkerCountGivesTheDefaultCount() =>
        new JoltBackendOptions { WorkerThreads = -1 }
            .WorkerThreadsFor(processorCount: 4)
            .Should()
            .Be(3, "Jolt sizes its workers from the hardware when it gets a count of zero or less");

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
/// A backend's Jolt worker threads, which never follow the hardware, and their count by unique thread IDs, which a thread
/// that ends does not lower. On macOS nothing marks a Jolt worker, so a count allows one thread more, which the runtime can
/// start. Linux, untried, counts alike (ter-zrk6).
/// </summary>
[Collection(nameof(JoltThreadCountTests))]
public sealed class JoltThreadCountTests
{
    /// <summary>
    /// The flavor of <c>proc_pidinfo</c> that lists the kernel's unique 64-bit thread IDs. It is private to XNU
    /// (bsd/sys/proc_info_private.h), and the SDK's sys/proc_info.h leaves it out. If macOS changes the value, the IDs
    /// fail to load or come out wrong.
    /// </summary>
    private const int ProcPidListThreadIds = 28;

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

    [Fact]
    public void AThreadThatEndsWhileABackendStartsLowersNoCount() =>
        ThreadsStartedBetween(before: [1, 2, 3], after: [1, 2, 4, 5])
            .Should()
            .Be(2, "Jolt started threads 4 and 5, and thread 3 ended");

    [Fact]
    public void AThreadThatStartsAfterAnotherEndedHasAnIdOfItsOwn()
    {
        HashSet<ulong> first = IdsNewWhileAThreadRuns();
        HashSet<ulong> second = IdsNewWhileAThreadRuns();

        second.Should().NotBeEmpty("the second thread runs while the IDs are taken");
        second.Should().NotIntersectWith(first, "an ID that came back would hide a new thread from the count");
    }

    // The threads of the process that a backend with these options adds while it lives: the IDs present after it starts
    // and absent before, so that a thread that ends in the meantime, such as a worker of the disposed backend that the
    // kernel still lists, lowers no count. The first backend of the process also initializes Jolt, so a backend built and
    // disposed first keeps that out of the count.
    private static int ThreadsStartedBy(JoltBackendOptions options)
    {
        new JoltPhysicsBackend(new() { WorkerThreads = 1 }).Dispose();
        HashSet<ulong> before = ThreadIds();
        using var backend = new JoltPhysicsBackend(options);
        return ThreadsStartedBetween(before, ThreadIds());
    }

    private static int ThreadsStartedBetween(HashSet<ulong> before, HashSet<ulong> after) =>
        after.Except(before).Count();

    // The IDs that are present while a new thread runs and absent before it starts: the new thread's ID, and the ID of any
    // thread that the runtime starts in the meantime.
    private static HashSet<ulong> IdsNewWhileAThreadRuns()
    {
        using var running = new ManualResetEventSlim();
        using var end = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            running.Set();
            end.Wait();
        });
        HashSet<ulong> before = ThreadIds();
        thread.Start();
        running.Wait();
        HashSet<ulong> during = ThreadIds();
        end.Set();
        thread.Join();
        during.ExceptWith(before);
        return during;
    }

    // The IDs of the threads of the process. On macOS, ProcessThread.Id is not unique over time: a thread that starts can
    // take the ID of a thread that just ended. So the kernel's 64-bit thread IDs take its place there. On Linux it is the
    // kernel's thread ID, which the kernel gives out again only after it wraps around pid_max.
    private static HashSet<ulong> ThreadIds()
    {
        if (OperatingSystem.IsMacOS())
            return MacThreadIds();
        using var process = Process.GetCurrentProcess();
        return [.. process.Threads.Cast<ProcessThread>().Select(thread => (ulong)thread.Id)];
    }

    private static HashSet<ulong> MacThreadIds()
    {
        // proc_pidinfo fills at most the buffer that it gets, so a full buffer can hide threads, and a larger one follows.
        for (int capacity = 256; ; capacity *= 2)
        {
            ulong[] ids = new ulong[capacity];
            int bytes = ProcPidInfo(
                pid: Environment.ProcessId,
                flavor: ProcPidListThreadIds,
                arg: 0,
                buffer: ids,
                bufferSize: capacity * sizeof(ulong)
            );
            if (bytes <= 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (bytes < capacity * sizeof(ulong))
                return [.. ids[..(bytes / sizeof(ulong))]];
        }
    }

    [DllImport("libproc.dylib", EntryPoint = "proc_pidinfo", SetLastError = true)]
    private static extern int ProcPidInfo(int pid, int flavor, ulong arg, [Out] ulong[] buffer, int bufferSize);
}
