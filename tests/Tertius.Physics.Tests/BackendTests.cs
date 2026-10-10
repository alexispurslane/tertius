using System.Numerics;

namespace Tertius.Physics.Tests;

public class BackendTests
{
    [Fact]
    public void MisuseThrowsClearErrors()
    {
        using var h = new PhysicsHarness();
        h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        h.Sync();
        JoltPhysicsBackend backend = h.Backend;

        backend.Invoking(b => b.ApplyChanges(null!, h.Frames)).Should().Throw<ArgumentNullException>();
        backend.Invoking(b => b.ApplyChanges(new PhysicsChanges(), null!)).Should().Throw<ArgumentNullException>();
        backend.Invoking(b => b.Step(Context(h, 0f), h.Output)).Should().Throw<ArgumentOutOfRangeException>();
        backend.Invoking(b => b.Step(Context(h, float.NaN), h.Output)).Should().Throw<ArgumentOutOfRangeException>();
        backend.Invoking(b => b.Step(Context(h, PhysicsHarness.Dt), null!)).Should().Throw<ArgumentNullException>();
        backend.Invoking(b => b.Step(default, h.Output)).Should().Throw<ArgumentNullException>();

        // After Dispose, the backend refuses work instead of touching freed Jolt objects.
        backend.Dispose();
        backend.Invoking(b => b.ApplyChanges(new PhysicsChanges(), h.Frames)).Should().Throw<ObjectDisposedException>();
        backend.Invoking(b => b.Step(Context(h, PhysicsHarness.Dt), h.Output)).Should().Throw<ObjectDisposedException>();
        backend.Invoking(b => b.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void SceneWithMoreFramesThanTheFirstTablesHoldKeepsEveryBodyInItsFrame()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        // A ball in space makes the step look at every child bubble of space.
        Entity probe = h.AddBody(space, Shapes.Ball(0.5f), Shapes.At(0, -100, 0));
        var rooms = new List<(FrameHandle Frame, Entity Ball)>();
        // The backend's tables start with room for 16 frames.
        for (int i = 0; i < 20; i++)
        {
            FrameHandle room = h.AddFrame(space, Shapes.At(i * 50, 0, 0), FrameBounds.Sphere(10), MotionDriver.Static);
            rooms.Add((room, h.AddBody(room, Shapes.Ball(0.5f), Shapes.At(0, 0, 0), velocity: new Vector3(0, 1, 0))));
        }

        h.Step(3);

        h.Backend.SystemCount.Should().Be(21);
        h[probe].Frame.Should().Be(space);
        foreach (var (room, ball) in rooms)
        {
            h[ball].Frame.Should().Be(room);
            h[ball].Pose.Position.Y.Should().BeApproximately(3 * PhysicsHarness.Dt, 1e-6);
        }
        h.HandOffs.Should().BeEmpty();
    }

    [Fact]
    public void FramesWithoutBodiesPastTheFirstTablesCostNothingAndCanBeDestroyed()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        var rooms = new List<FrameHandle>();
        for (int i = 0; i < 45; i++)
            rooms.Add(h.AddFrame(space, Shapes.At(i * 50, 0, 0), FrameBounds.Sphere(10), MotionDriver.Static));
        // Only one room, far past the first tables, gets a body and so a system.
        FrameHandle occupied = rooms[39];
        Entity ball = h.AddBody(occupied, Shapes.Ball(0.5f), Shapes.At(0, 0, 0), velocity: new Vector3(0, 1, 0));
        h.Step();
        h.Backend.SystemCount.Should().Be(1);
        h[ball].Frame.Should().Be(occupied);

        // The empty rooms go, on both sides of it. They never had systems, so there is nothing to remove and
        // nothing to warn about.
        foreach (FrameHandle room in rooms.Where(r => r != occupied))
        {
            h.Frames.Destroy(room);
            h.Changes.FramesDestroyed.Add(room);
        }
        h.Step();
        h.Backend.SystemCount.Should().Be(1);
        h[ball].Pose.Position.Y.Should().BeApproximately(2 * PhysicsHarness.Dt, 1e-6);
        h.Warnings.Should().BeEmpty();
    }

    private static PhysicsStepContext Context(PhysicsHarness h, float dt) =>
        new(h.Tick, dt, h.Time, h.Frames, ReadOnlyMemory<PhysicsIntent>.Empty, ReadOnlyMemory<Entity>.Empty);
}
