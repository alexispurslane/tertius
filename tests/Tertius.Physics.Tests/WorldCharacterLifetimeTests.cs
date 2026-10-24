using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A character is a native Jolt object with an inner body in its frame's system. The inner body lets rigid bodies collide
/// with the character. When the character leaves a system, by a destroy, a hand-off or a growth, or when the backend goes,
/// the backend destroys the native character, and the character's destructor takes the inner body out of the system.
/// </summary>
public sealed class WorldCharacterLifetimeTests
{
    private static readonly BodyDesc Person = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character,
        Mass: 70);

    private static bool IsInner(JoltPhysicsBackend.BodyFacts facts) =>
        BodyUserData.Kind(facts.UserData) == BodyKind.CharacterInner;

    /// <summary>
    /// A small box sleeps on the head of a character. The world destroys the character. Its inner body leaves the system,
    /// so nothing holds the box up, and it falls straight down onto the floor within a second.
    /// </summary>
    [Fact]
    public void DestroyingACharacterRemovesItsInnerBodySoTheBoxOnItsHeadFallsStraightDown()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity person = world.Spawn(new() { Frame = ground, Pose = At(0, 0.95), Body = Person });
        Entity box = world.Spawn(new()
            { Frame = ground, Pose = At(0, 2.2), Body = new(Shape: new BoxShapeDesc(new(0.2f)), Mass: 1) });
        world.Tick();
        TickUntil(world, () => !IsAwake(backend, ground, box), 600);
        IsAwake(backend, ground, box).Should().BeFalse("the box fell asleep on the character's head");
        world.PoseInOwnFrame(box).Position.Y.Should()
            .BeGreaterThan(1.9, "the box rests on the head, which is 1.82 m above the floor");

        world.Destroy(person);
        Tick(world, 60);

        backend.BodiesIn(ground).Should()
            .NotContain(facts => IsInner(facts), "the destroyed character took its inner body with it");
        DPose pose = world.PoseInOwnFrame(box);
        pose.Position.Y.Should().BeApproximately(0.2, 0.01, "the box fell onto the floor");
        pose.Position.X.Should().BeApproximately(0, 0.05, "nothing stood in its way");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A character walks from the ground frame into the bubble of a shed, and 3 m on. A ball then falls on the ground frame
    /// where the character crossed. The character's inner body left the ground frame's system with the character, so the ball falls
    /// straight onto the floor and neither the ball nor the character hears a collision with the other.
    /// </summary>
    [Fact]
    public void ACharacterThatChangesFrameLeavesNoInnerBodyBehind()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        // The shed's bubble covers x from 5 to 15. Its interior is a slab whose top is level with the ground's floor.
        var slab = new CompoundShapeDesc([
            new(new BoxShapeDesc(new(6, 0.5f, 6)), new(new(0, -1.5f, 0), Quaternion.Identity))
        ]);
        Entity shedOwner = world.Spawn(new()
        {
            Frame = ground,
            Pose = At(10, 1),
            Body = new(Shape: new BoxShapeDesc(new(0.2f)), Motion: BodyMotion.Static, Mass: 1000, InteriorShape: slab),
            AsFrame = new(FrameBounds.Sphere(5), GravityField.Uniform(new(0, -9.81f, 0)))
        });
        FrameHandle shed = world.GetOwnedFrame(shedOwner);
        var pilot = new Pilot { EveryTick = body => body.MoveCharacter(new(2, 0, 0)) };
        var personLog = new CollisionLog();
        Entity person = world.Spawn(new()
            { Frame = ground, Pose = At(0, 0.95), Body = Person, Components = [pilot, personLog] });
        TickUntil(world, () => world.GetFrame(person) == shed, 600);
        world.GetFrame(person).Should().Be(shed, "the character walked into the shed's bubble");
        // On 3 m, so that the character keeps no ghost in the ground frame near the boundary.
        Tick(world, 90);
        pilot.EveryTick = body => body.MoveCharacter(Vector3.Zero);
        Tick(world, 10);
        backend.GhostCount(person).Should().Be(0);

        backend.BodiesIn(ground).Should().NotContain(facts => IsInner(facts),
            "the character's inner body went with it into the shed");
        backend.BodiesIn(shed).Count(IsInner).Should().Be(1);

        var ballLog = new CollisionLog();
        Entity ball = world.Spawn(new()
        {
            Frame = ground, Pose = At(4.6, 3.5), Body = new(Shape: new SphereShapeDesc(0.2f), Mass: 1),
            Components = [ballLog]
        });
        Tick(world, 120);

        ballLog.CountWith(person).Should().Be(0, "the character stands in the shed, far from the ball");
        personLog.CountWith(ball).Should().Be(0);
        world.GetFrame(ball).Should().Be(ground);
        world.PoseInOwnFrame(ball).Position.X.Should().BeApproximately(4.6, 0.05, "the ball fell straight down");
        world.PoseInOwnFrame(ball).Position.Y.Should().BeApproximately(0.2, 0.01, "the ball rests on the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Two characters stand in a system of at most 8 bodies. Crates fill it, so it grows. The growth makes new native
    /// characters in the new system and frees the old ones, and it frees the old system. The characters still stand.
    /// </summary>
    [Fact]
    public void AGrowthFreesTheOldNativeCharactersAndTheOldSystem()
    {
        var backend = new JoltPhysicsBackend(new() { WorkerThreads = 2, MaxBodiesPerFrame = 8 });
        using var world = new World(new() { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true },
            backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity first = world.Spawn(new() { Frame = ground, Pose = At(0, 0.95), Body = Person });
        Entity second = world.Spawn(new() { Frame = ground, Pose = At(3, 0.95), Body = Person });
        Tick(world, 10);
        backend.CapacityOf(ground).Should().Be(8);
        WeakReference[] oldNatives =
        [
            NativeListenerOf(backend.CharacterOf(first)!), NativeListenerOf(backend.CharacterOf(second)!),
            NativeListenerOf(backend.SystemOf(ground)!)
        ];

        for (int i = 0; i < 8; i++)
            world.Spawn(new() { Frame = ground, Pose = At(3 * i - 12, 0.5, 10), Body = Cube() });
        Tick(world, 10);
        backend.CapacityOf(ground).Should().Be(16, "the crates filled the system, so it grew");

        CollectGarbage();
        oldNatives.Should()
            .AllSatisfy(native => native.IsAlive.Should().BeFalse("the growth freed the old native object"));
        backend.BodiesIn(ground).Count(IsInner).Should().Be(2);
        world.PoseInOwnFrame(first).Position.Y.Should()
            .BeApproximately(0.9, 0.03, "the character still stands on the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>Disposing the world disposes its backend, which frees every native character and every system.</summary>
    [Fact]
    public void DisposingTheBackendFreesEveryNativeCharacterAndSystem()
    {
        WeakReference[] natives = RunAWorldAndDisposeIt();

        CollectGarbage();
        natives.Should().AllSatisfy(native =>
            native.IsAlive.Should().BeFalse("the backend freed the native object when it was disposed"));
    }

    /// <summary>A world with a character in a root frame and one in a child frame, disposed. Returns their native listeners.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] RunAWorldAndDisposeIt()
    {
        World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        FrameHandle room = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = ground,
            Pose = At(50, 1),
            Body = new(Shape: new BoxShapeDesc(new(0.2f)), Motion: BodyMotion.Static, Mass: 1000,
                InteriorShape: new BoxShapeDesc(new(4, 0.5f, 4))),
            AsFrame = new(FrameBounds.Sphere(10), GravityField.Uniform(new(0, -9.81f, 0)))
        }));
        Entity outside = world.Spawn(new() { Frame = ground, Pose = At(0, 0.95), Body = Person });
        Entity inside = world.Spawn(new() { Frame = room, Pose = At(0, 1.45), Body = Person });
        Tick(world, 10);
        WeakReference[] natives =
        [
            NativeListenerOf(backend.CharacterOf(outside)!), NativeListenerOf(backend.CharacterOf(inside)!),
            NativeListenerOf(backend.SystemOf(ground)!), NativeListenerOf(backend.SystemOf(room)!)
        ];
        world.Dispose();
        return natives;
    }

    /// <summary>
    /// A weak reference to the managed listener that JoltPhysicsSharp keeps for a native character or physics system. The
    /// native object calls back through it, so JoltPhysicsSharp holds it with a GC handle, and it frees that handle in the
    /// call that destroys the native object, and nowhere else. So after a full collection the listener is gone exactly
    /// when the native object is gone.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference NativeListenerOf(NativeObject native)
    {
        FieldInfo field = native.GetType().GetField("_listenerUserData", BindingFlags.NonPublic | BindingFlags.Instance)
                          ?? throw new InvalidOperationException(
                              $"JoltPhysicsSharp's {native.GetType().Name} has no _listenerUserData field. Check this test after an upgrade.");
        IntPtr handle = (nint)field.GetValue(native)!;
        return new(GCHandle.FromIntPtr(handle).Target);
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
