using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

public sealed class BackendTests
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
        FrameHandle space = frames.Create(
            frameOwner: new(1, 1),
            parent: FrameHandle.Null,
            localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e6f),
            field: default
        );
        changes.FramesCreated.Add(space);
        using var backend = new JoltPhysicsBackend(new() { WorkerThreads = 2 });
        backend.ApplyChanges(changes, frames);
        changes.Clear();
        frames.Update();

        backend.Invoking(b => b.ApplyChanges(null!, frames)).Should().Throw<ArgumentNullException>();
        backend.Invoking(b => b.ApplyChanges(new(), null!)).Should().Throw<ArgumentNullException>();
        backend.Invoking(b => b.Step(Context(frames, 0f), output)).Should().Throw<ArgumentOutOfRangeException>();
        backend.Invoking(b => b.Step(Context(frames, float.NaN), output)).Should().Throw<ArgumentOutOfRangeException>();
        backend.Invoking(b => b.Step(Context(frames, Dt), null!)).Should().Throw<ArgumentNullException>();
        backend.Invoking(b => b.Step(default, output)).Should().Throw<ArgumentNullException>();

        // After Dispose, the backend refuses work instead of touching freed Jolt objects.
        backend.Dispose();
        backend.Invoking(b => b.ApplyChanges(new(), frames)).Should().Throw<ObjectDisposedException>();
        backend.Invoking(b => b.Step(Context(frames, Dt), output)).Should().Throw<ObjectDisposedException>();
        backend.Invoking(b => b.Dispose()).Should().NotThrow();
    }

    /// <summary>A query filter that accepts every entity.</summary>
    private sealed class AcceptsEveryEntity : IPhysicsQueryFilter
    {
        public bool Accepts(Entity entity) => true;
    }

    /// <summary>
    /// The World refuses a shape that no query can use before it asks the backend, so the backend's own refusal is for
    /// callers that skip the World. This test sets up a frame with one box through the World, then calls the real backend
    /// itself. It shows that a usable shape finds the box through the same calls on the same frame, so that the refusal
    /// comes from the shape and not from a frame without a system.
    /// </summary>
    [Fact]
    public void ShapeCastAndOverlapRefuseAShapeThatNoQueryCanUse()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 0);
        Entity box = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(5),
                Body = new(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Static),
            }
        );
        world.Tick();
        var filter = new AcceptsEveryEntity();
        var unusable = QueryShape.Sphere(0);
        var usable = QueryShape.Sphere(1);
        var shapeHits = new List<ShapeCastHit>();
        var overlapped = new List<Entity>();

        backend
            .Invoking(b =>
                b.CastShape(
                    frame: ground,
                    shape: unusable,
                    start: DPose.Identity,
                    direction: Double3.UnitX,
                    startDistance: 0,
                    endDistance: 10,
                    filter: filter,
                    hits: shapeHits
                )
            )
            .Should()
            .Throw<ArgumentException>()
            .WithParameterName("shape")
            .WithMessage("*is a sphere with a radius of 0 m*");
        backend
            .Invoking(b =>
                b.Overlap(frame: ground, shape: unusable, pose: DPose.Identity, filter: filter, entities: overlapped)
            )
            .Should()
            .Throw<ArgumentException>()
            .WithParameterName("shape")
            .WithMessage("*is a sphere with a radius of 0 m*");

        backend.CastShape(
            frame: ground,
            shape: usable,
            start: DPose.Identity,
            direction: Double3.UnitX,
            startDistance: 0,
            endDistance: 10,
            filter: filter,
            hits: shapeHits
        );
        backend.Overlap(frame: ground, shape: usable, pose: At(5), filter: filter, entities: overlapped);
        shapeHits.Should().ContainSingle().Which.Entity.Should().Be(box);
        overlapped.Should().Equal(box);
    }

    [Fact]
    public void SceneWithMoreFramesThanTheFirstTablesHoldKeepsEveryBodyInItsFrame()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        // A ball in space makes the step look at every child bubble of space.
        Entity probe = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(0, -100),
                Body = Ball(),
            }
        );
        var rooms = new List<(FrameHandle Frame, Entity Ball)>();
        // The backend's tables start with room for 16 frames.
        for (int i = 0; i < 20; i++)
        {
            FrameHandle room = world.GetOwnedFrame(
                world.Spawn(
                    new()
                    {
                        Frame = space,
                        Pose = At(i * 50),
                        AsFrame = new(FrameBounds.Sphere(10)),
                    }
                )
            );
            Entity ball = world.Spawn(
                new()
                {
                    Frame = room,
                    Pose = At(0, 0, 0),
                    LinearVelocity = new Vector3(0, 1, 0),
                    Body = Ball(),
                }
            );
            rooms.Add((room, ball));
        }

        Tick(world, 3);

        faults.Jolt.SystemCount.Should().Be(21);
        world.GetFrame(probe).Should().Be(space);
        foreach ((FrameHandle room, Entity ball) in rooms)
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
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults, warnings: warnings);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        var roomOwners = new List<Entity>();
        for (int i = 0; i < 45; i++)
            roomOwners.Add(
                world.Spawn(
                    new()
                    {
                        Frame = space,
                        Pose = At(i * 50),
                        AsFrame = new(FrameBounds.Sphere(10)),
                    }
                )
            );
        // Only one room, far past the first tables, gets a body and so a system.
        FrameHandle occupied = world.GetOwnedFrame(roomOwners[39]);
        Entity ball = world.Spawn(
            new()
            {
                Frame = occupied,
                Pose = At(0, 0, 0),
                LinearVelocity = new Vector3(0, 1, 0),
                Body = Ball(),
            }
        );
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
        new(
            tick: 0,
            deltaTime: dt,
            time: 0,
            frames: frames,
            intents: ReadOnlyMemory<PhysicsIntent>.Empty,
            intentEntities: ReadOnlyMemory<Entity>.Empty
        );
}
