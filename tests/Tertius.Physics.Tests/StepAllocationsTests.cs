using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Runs the tests of <see cref="StepAllocationsTests"/> alone, because a <see cref="StepAllocations"/> swaps joltc's
/// contact callbacks for the whole process.
/// </summary>
[CollectionDefinition(nameof(StepAllocationsTests), DisableParallelization = true)]
public sealed class StepAllocationsCollection;

/// <summary>
/// <see cref="StepAllocations"/> counts what a window allocates on the threads that run the work of a world's steps, and
/// nothing that another thread of the process allocates in the same window.
/// </summary>
[Collection(nameof(StepAllocationsTests))]
public sealed unsafe class StepAllocationsTests
{
    private const int Kilobyte = 1024;
    private const int Megabyte = 1024 * Kilobyte;

    // The bytes of the array that each callback of AllocatingCallbacks allocates. The array object is larger still.
    private const int CallbackPayload = 64;

    // The contact callbacks that a test runs on one thread: a megabyte of payload, far more than the window's own
    // allocations on the ticking thread.
    private const int CallsOnAThread = 16 * Kilobyte;

    // A table of contact callbacks that allocate CallbackPayload bytes each and report nothing to the world. joltc reads
    // it through the counter, so it lives as long as the process, as the counter's own table does.
    private static readonly ContactListenerProcs* AllocatingCallbacks = NewAllocatingCallbacks();

    // The calls that AllocatingCallbacks received, and the array that the latest one allocated.
    private static int allocatingCalls;
    private static byte[]? callbackArray;

    /// <summary>The window runs on the thread that ticks the world, so what the window allocates counts.</summary>
    [Fact]
    public void AnAllocationOnTheTickingThreadCounts()
    {
        using World world = NewWorldWithStepAllocations(out StepAllocations steps, out _);
        Falling(world);
        Tick(world, 10);
        byte[]? kept = null;

        long bytes = steps.Bytes(() =>
        {
            world.Tick();
            kept = new byte[Kilobyte];
        });

        kept.Should().NotBeNull();
        bytes.Should().BeGreaterThanOrEqualTo(Kilobyte);
    }

    /// <summary>
    /// A thread that the test starts runs no work of the step, so the megabyte that it allocates while the world ticks
    /// does not count, as the finalizer thread's trim of the shared array pool must not.
    /// </summary>
    [Fact]
    public void AnAllocationOnAThreadThatRunsNoWorkOfTheStepDoesNotCount()
    {
        using World world = NewWorldWithStepAllocations(out StepAllocations steps, out _);
        Falling(world);
        Tick(world, 10);
        byte[]? kept = null;
        var other = new Thread(() => kept = new byte[Megabyte]);

        long bytes = steps.Bytes(() =>
        {
            other.Start();
            world.Tick();
            other.Join();
        });

        kept.Should().NotBeNull();
        bytes.Should().BeLessThan(Megabyte, "the megabyte was allocated on a thread that runs no work of the step");
    }

    /// <summary>
    /// The edit of a step's output runs on the physics thread, inside the step, so the megabyte that it allocates counts,
    /// though the ticking thread did not allocate it.
    /// </summary>
    [Fact]
    public void AnAllocationOnThePhysicsThreadDuringAStepCounts()
    {
        var faults = new FaultInjectingBackend(new(JoltBackendOptions.Unpaced));
        using World world = NewWorldWithStepAllocations(faults, out StepAllocations steps);
        Falling(world);
        Tick(world, 10);
        byte[]? kept = null;
        faults.EditNextOutput = _ => kept = new byte[Megabyte];

        long bytes = steps.Bytes(world.Tick);

        kept.Should().NotBeNull();
        bytes.Should().BeGreaterThanOrEqualTo(Megabyte, "the physics thread allocated the megabyte during the step");
    }

    /// <summary>
    /// Jolt runs the contact callbacks of a step on its job threads and on the physics thread, so what each callback
    /// allocates counts on whichever thread runs it: here 100 boxes land on a floor and jostle.
    /// </summary>
    [Fact]
    public void WhatAContactCallbackAllocatesCountsOnWhicheverThreadRunsIt()
    {
        using World world = NewWorldWithStepAllocations(
            new JoltPhysicsBackend(JoltBackendOptions.Unpaced),
            AllocatingCallbacks,
            out StepAllocations steps
        );
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        for (int i = 0; i < 100; i++)
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = At(i % 10, 1, i / 10),
                    Body = Cube(),
                }
            );
        int callsBefore = Volatile.Read(ref allocatingCalls);

        long bytes = steps.Bytes(() => Tick(world, 60));

        long calls = Volatile.Read(ref allocatingCalls) - callsBefore;
        calls.Should().BePositive("the boxes touch the floor and each other in the window");
        bytes.Should().BeGreaterThanOrEqualTo(calls * CallbackPayload, "every contact callback allocated an array");
    }

    /// <summary>
    /// A contact callback that a thread other than the physics thread runs counts, as one on a Jolt job thread does. The
    /// test calls the counting wrappers from a thread of its own, so the count does not depend on where Jolt runs its
    /// callbacks. The thread starts before the window, so that its start does not count on the ticking thread.
    /// </summary>
    [Fact]
    public void WhatAContactCallbackAllocatesOnAThreadOtherThanThePhysicsThreadCounts()
    {
        using World world = NewWorldWithStepAllocations(
            new JoltPhysicsBackend(JoltBackendOptions.Unpaced),
            AllocatingCallbacks,
            out StepAllocations steps
        );
        Falling(world);
        // The steps name the physics thread, which lives as long as the world, so no thread that the test starts shares
        // its id.
        Tick(world, 10);
        using var go = new ManualResetEventSlim();
        var job = new Thread(() =>
        {
            go.Wait();
            for (int i = 0; i < CallsOnAThread; i++)
                StepAllocations.CountingCallbacks->OnContactRemoved(0, null);
        })
        {
            IsBackground = true,
        };
        job.Start();
        int callsBefore = Volatile.Read(ref allocatingCalls);

        long bytes = steps.Bytes(() =>
        {
            go.Set();
            job.Join();
        });

        (Volatile.Read(ref allocatingCalls) - callsBefore).Should().Be(CallsOnAThread);
        bytes.Should()
            .BeGreaterThanOrEqualTo(
                (long)CallsOnAThread * CallbackPayload,
                "each callback on the test's thread allocated an array"
            );
    }

    /// <summary>
    /// The physics thread's count of a step holds what a contact callback allocates on that thread, so the callback adds
    /// it once, not twice. The edit of the step's output calls the counting wrappers on the physics thread, so the test
    /// does not depend on where Jolt runs its callbacks.
    /// </summary>
    [Fact]
    public void WhatAContactCallbackAllocatesOnThePhysicsThreadCountsOnce()
    {
        var faults = new FaultInjectingBackend(new(JoltBackendOptions.Unpaced));
        using World world = NewWorldWithStepAllocations(faults, AllocatingCallbacks, out StepAllocations steps);
        Falling(world);
        Tick(world, 10);
        faults.EditNextOutput = _ =>
        {
            for (int i = 0; i < CallsOnAThread; i++)
                StepAllocations.CountingCallbacks->OnContactRemoved(0, null);
        };

        long bytes = steps.Bytes(world.Tick);

        // An array of CallbackPayload bytes is larger than CallbackPayload but smaller than twice that, so the arrays
        // counted twice reach this bound and the arrays counted once leave room for the rest of the step.
        bytes.Should()
            .BeGreaterThanOrEqualTo((long)CallsOnAThread * CallbackPayload)
            .And.BeLessThan(
                2L * CallsOnAThread * CallbackPayload,
                "the step's count on the physics thread already holds the arrays"
            );
    }

    /// <summary>
    /// A disposed counter gives joltc the backend's own contact callbacks again, also when its wrappers called others, so
    /// a world made after it hears its contacts.
    /// </summary>
    [Fact]
    public void AWorldMadeAfterACounterThatCalledOtherCallbacksHearsItsContacts()
    {
        NewWorldWithStepAllocations(
                new JoltPhysicsBackend(JoltBackendOptions.Unpaced),
                AllocatingCallbacks,
                out _
            )
            .Dispose();
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, 1),
                Body = Cube(),
            }
        );

        Tick(world, 60);

        faults.Outputs.SelectMany(output => output.Contacts).Should().NotBeEmpty("the cube lands on the floor");
    }

    /// <summary>
    /// A counter swaps joltc's contact callbacks for the whole process until it is disposed, so a second one while it
    /// lives would restore the wrong callbacks when it is disposed first.
    /// </summary>
    [Fact]
    public void ASecondCounterIsRefusedWhileTheFirstLives()
    {
        using World world = NewWorldWithStepAllocations(out _, out _);

        Action second = () => NewWorldWithStepAllocations(out _, out _).Dispose();

        second.Should().Throw<InvalidOperationException>().WithMessage("*already*");
    }

    /// <summary>
    /// The backend is the counter's from the call on, so a counter that is refused disposes it, and its Jolt threads do
    /// not outlive the refusal.
    /// </summary>
    [Fact]
    public void ARefusedCounterDisposesTheBackendThatItWasGiven()
    {
        using World world = NewWorldWithStepAllocations(out _, out _);
        var jolt = new JoltPhysicsBackend(JoltBackendOptions.Unpaced);
        Action second = () => NewWorldWithStepAllocations(jolt, out _).Dispose();
        second.Should().Throw<InvalidOperationException>();

        Action use = () => jolt.ShapeProblem(new SphereShapeDesc(0.5f));

        use.Should().Throw<ObjectDisposedException>("the refused counter disposed the backend");
    }

    /// <summary>
    /// The counter wraps the added, persisted and removed callbacks. A validate callback would run uncounted on Jolt's
    /// threads, so a table with one is refused.
    /// </summary>
    [Fact]
    public void ATableWithAValidateCallbackIsRefused()
    {
        ContactListenerProcs* withValidate = stackalloc ContactListenerProcs[1];
        *withValidate = *AllocatingCallbacks;
        withValidate->OnContactValidate = 1;

        Action counted = () =>
            NewWorldWithStepAllocations(new JoltPhysicsBackend(JoltBackendOptions.Unpaced), withValidate, out _)
                .Dispose();

        counted.Should().Throw<InvalidOperationException>().WithMessage("*validate*");
    }

    /// <summary>A ball that falls in a frame with gravity, so that every step has an awake body.</summary>
    private static void Falling(World world)
    {
        FrameHandle ground = Ground(world, 9.81f);
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, 1_000),
                Body = new(Shape: new SphereShapeDesc(0.5f), Mass: 1),
            }
        );
    }

    private static ContactListenerProcs* NewAllocatingCallbacks()
    {
        var callbacks = (ContactListenerProcs*)NativeMemory.AllocZeroed((nuint)sizeof(ContactListenerProcs));
        callbacks->OnContactAdded = &AllocateOnContact;
        callbacks->OnContactPersisted = &AllocateOnContact;
        callbacks->OnContactRemoved = &AllocateOnRemoved;
        return callbacks;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void AllocateOnContact(nint _, nint _1, nint _2, nint _3, ContactSettings* _4) => Allocate();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void AllocateOnRemoved(nint _, SubShapeIDPair* _1) => Allocate();

    private static void Allocate()
    {
        Volatile.Write(ref callbackArray, new byte[CallbackPayload]);
        Interlocked.Increment(ref allocatingCalls);
    }
}
