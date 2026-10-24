using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Several backends in one process step at the same time on different threads, as parallel tests do. joltc gives each
/// calling thread its own temp allocator (native/joltc-thread-temp-allocator.patch), and each backend serializes only its
/// own work with its gate, so no backend waits for another and none corrupts another's step.
/// </summary>
public sealed class ConcurrentBackendsTests
{
    private const int Worlds = 4;
    private const int Ticks = 180;

    [Fact]
    public void WorldsThatStepAtTheSameTimeOnSeveralThreadsEndAsAWorldThatStepsAlone()
    {
        ulong alone = HeapAndCharacterRun();

        ulong[] hashes = new ulong[Worlds];
        using var start = new Barrier(Worlds);
        Thread[] threads =
        [
            .. Enumerable.Range(0, Worlds).Select(i => new Thread(() =>
            {
                start.SignalAndWait();
                hashes[i] = HeapAndCharacterRun();
            }))
        ];
        foreach (Thread thread in threads)
            thread.Start();
        foreach (Thread thread in threads)
            thread.Join();

        hashes.Should().AllBeEquivalentTo(alone,
            "a step uses only its own thread's temp allocator and its own backend's state");
    }

    /// <summary>
    /// The hash of a world after three seconds: 24 cubes that fall in a heap onto a floor, and a character capsule that
    /// stands beside them. The heap makes many contacts and the character makes character updates, the two kinds of
    /// native call that use joltc's temp allocator.
    /// </summary>
    private static ulong HeapAndCharacterRun()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        for (int i = 0; i < 24; i++)
            world.Spawn(new() { Frame = ground, Pose = At(i % 3 * 0.6, 1 + i * 1.1, i / 3 % 2 * 0.6), Body = Cube() });
        world.Spawn(new()
        {
            Frame = ground,
            Pose = At(4, 0.91),
            Body = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70)
        });
        for (int tick = 0; tick < Ticks; tick++)
            world.Tick();
        return WorldHash.Of(world);
    }
}
