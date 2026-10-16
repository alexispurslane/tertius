using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

public class BackendTests
{
    private const float Dt = 1f / 60f;

    /// <summary>
    /// The backend refuses arguments that the World never passes it, so this test calls the real backend itself, with a
    /// frame tree, a change batch and an output that it builds by hand. No stand-in is involved.
    /// </summary>
    [Fact]
    public void MisuseThrowsClearErrors()
    {
        var frames = new FrameTree();
        var changes = new PhysicsChanges();
        var output = new PhysicsStepOutput();
        FrameHandle space = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static, default, default);
        changes.FramesCreated.Add(space);
        using var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 });
        backend.ApplyChanges(changes, frames);
        changes.Clear();
        frames.Update();

        backend.Invoking(b => b.ApplyChanges(null!, frames)).Should().Throw<ArgumentNullException>();
        backend.Invoking(b => b.ApplyChanges(new PhysicsChanges(), null!)).Should().Throw<ArgumentNullException>();
        backend.Invoking(b => b.Step(Context(frames, 0f), output)).Should().Throw<ArgumentOutOfRangeException>();
        backend.Invoking(b => b.Step(Context(frames, float.NaN), output)).Should().Throw<ArgumentOutOfRangeException>();
        backend.Invoking(b => b.Step(Context(frames, Dt), null!)).Should().Throw<ArgumentNullException>();
        backend.Invoking(b => b.Step(default, output)).Should().Throw<ArgumentNullException>();

        // After Dispose, the backend refuses work instead of touching freed Jolt objects.
        backend.Dispose();
        backend.Invoking(b => b.ApplyChanges(new PhysicsChanges(), frames)).Should().Throw<ObjectDisposedException>();
        backend.Invoking(b => b.Step(Context(frames, Dt), output)).Should().Throw<ObjectDisposedException>();
        backend.Invoking(b => b.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void SceneWithMoreFramesThanTheFirstTablesHoldKeepsEveryBodyInItsFrame()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        // A ball in space makes the step look at every child bubble of space.
        Entity probe = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, -100), Body = Ball() });
        var rooms = new List<(FrameHandle Frame, Entity Ball)>();
        // The backend's tables start with room for 16 frames.
        for (int i = 0; i < 20; i++)
        {
            FrameHandle room = world.GetOwnedFrame(world.Spawn(new SpawnDesc
            {
                Frame = space,
                Pose = At(i * 50),
                AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
            }));
            Entity ball = world.Spawn(new SpawnDesc { Frame = room, Pose = At(0, 0, 0), LinearVelocity = new Vector3(0, 1, 0), Body = Ball() });
            rooms.Add((room, ball));
        }

        Tick(world, 3);

        faults.Jolt.SystemCount.Should().Be(21);
        world.GetFrame(probe).Should().Be(space);
        foreach (var (room, ball) in rooms)
        {
            world.GetFrame(ball).Should().Be(room);
            world.PoseInOwnFrame(ball).Position.Y.Should().BeApproximately(3 * Dt, 1e-6);
        }
        faults.Outputs.SelectMany(output => output.HandOffs).Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void FramesWithoutBodiesPastTheFirstTablesCostNothingAndCanBeDestroyed()
    {
        var warnings = new List<string>();
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults, warnings);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        var roomOwners = new List<Entity>();
        for (int i = 0; i < 45; i++)
        {
            roomOwners.Add(world.Spawn(new SpawnDesc
            {
                Frame = space,
                Pose = At(i * 50),
                AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
            }));
        }
        // Only one room, far past the first tables, gets a body and so a system.
        FrameHandle occupied = world.GetOwnedFrame(roomOwners[39]);
        Entity ball = world.Spawn(new SpawnDesc { Frame = occupied, Pose = At(0, 0, 0), LinearVelocity = new Vector3(0, 1, 0), Body = Ball() });
        world.Tick();
        faults.Jolt.SystemCount.Should().Be(1);
        world.GetFrame(ball).Should().Be(occupied);

        // The empty rooms go, on both sides of it. They never had systems, so there is nothing to remove and
        // nothing to warn about.
        foreach (Entity owner in roomOwners.Where(owner => owner != roomOwners[39]))
            world.Destroy(owner).Should().BeTrue();
        world.Tick();
        faults.Jolt.SystemCount.Should().Be(1);
        world.PoseInOwnFrame(ball).Position.Y.Should().BeApproximately(2 * Dt, 1e-6);
        warnings.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    private static PhysicsStepContext Context(FrameTree frames, float dt) =>
        new(0, dt, 0, frames, ReadOnlyMemory<PhysicsIntent>.Empty, ReadOnlyMemory<Entity>.Empty);
}
