using System.Numerics;
using AwesomeAssertions;
using Tertius.Physics;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A component spawns an attached entity during a tick, and it names the frame in which it saw the parent. In the step
/// of that same tick, physics can hand the parent into another frame, and the component cannot know that. The sync point
/// makes the new entity live in the frame of its parent at that moment, so the hand-off does not fail the spawn. The
/// scene runs the real World on the real Jolt backend: a ball drifts from the root frame into the bubble of a station.
/// </summary>
public class AttachedSpawnHandOffTests
{
    private static readonly Pose ChildOffset = new(new Vector3(0, 0, 2), Quaternion.Identity);
    private static readonly Pose GrandchildOffset = new(new Vector3(0, 1, 0), Quaternion.Identity);

    /// <summary>An entity that a component spawned, the tick of the spawn, and a log of the event lists with a frame change in them.</summary>
    private sealed record Spawn(long Tick, Entity Entity, Recorder Log)
    {
        /// <summary>How many <see cref="FrameChanged"/> events the entity heard.</summary>
        public int FrameChangesHeard => Log.Seen.Count(seen => seen.Event.Is<FrameChanged>());
    }

    /// <summary>
    /// Space is a root frame with no gravity. The station is a static frame 10 m along +X, with a bubble of radius 5 m.
    /// The ball, of radius 0.5 m, starts 3.5 m along +X and drifts at 6 m/s along +X, so it enters the bubble after about
    /// 10 ticks. While the ball sees itself in space, it spawns a child at <see cref="ChildOffset"/> on each tick, with the
    /// frame that it sees. On its first tick, each child spawns a grandchild at <see cref="GrandchildOffset"/> in the same
    /// way. So the child of the hand-off tick and the grandchild under the child of the tick before are both spawned in the
    /// tick of the hand-off.
    /// </summary>
    private sealed class Scene : IDisposable
    {
        public Scene()
        {
            var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 });
            World = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, backend);
            Space = World.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
            Station = World.SpawnStaticFrame(Space, TestWorld.At(10), radius: 5);
            Ball = World.Spawn(new SpawnDesc
            {
                Frame = Space,
                Pose = TestWorld.At(3.5),
                LinearVelocity = new Vector3(6, 0, 0),
                Body = new BodyDesc(new SphereShapeDesc(0.5f), LinearDamping: 0, AngularDamping: 0),
                Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Frame == Space)
                        Children.Add(SpawnAttached(self, ChildOffset, [ChildBehavior()]));
                })],
            });
        }

        public World World { get; }
        public FrameHandle Space { get; }
        public FrameHandle Station { get; }
        public Entity Ball { get; }
        public List<Spawn> Children { get; } = [];
        public List<Spawn> Grandchildren { get; } = [];

        /// <summary>Ticks until the ball is in the station. Returns the number of the tick whose sync point handed it off.</summary>
        public long RunUntilHandOff()
        {
            for (int i = 0; i < 60; i++)
            {
                long tick = World.TickNumber;
                World.Tick();
                if (World.GetFrame(Ball) == Station)
                    return tick;
            }
            throw new InvalidOperationException("The ball did not reach the station within 60 ticks.");
        }

        public void Dispose() => World.Dispose();

        /// <summary>Spawns an entity attached to the running entity, in the frame that the running entity sees, as Carry does.</summary>
        private static Spawn SpawnAttached(Self self, Pose offset, IComponent[] components)
        {
            var log = new Recorder(EventMask.Of<FrameChanged>());
            Entity spawned = self.Spawn(new SpawnDesc
            {
                Frame = self.Frame,
                AttachTo = self.Entity,
                AttachOffset = offset,
                Components = [log, .. components],
            });
            return new Spawn(self.Tick, spawned, log);
        }

        /// <summary>Spawns one grandchild on the first tick of its child.</summary>
        private Lambda ChildBehavior()
        {
            bool spawned = false;
            return Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                if (spawned)
                    return;
                spawned = true;
                Grandchildren.Add(SpawnAttached(self, GrandchildOffset, []));
            });
        }
    }

    [Fact]
    public void AChildSpawnedInTheTickOfItsParentsHandOffBecomesLiveInTheNewFrameAtItsOffset()
    {
        using var scene = new Scene();

        long handOff = scene.RunUntilHandOff();

        Spawn child = scene.Children.Should().ContainSingle(c => c.Tick == handOff, "the ball still saw itself in space then").Subject;
        scene.World.IsAlive(child.Entity).Should().BeTrue();
        scene.World.GetFrame(child.Entity).Should().Be(scene.Station);
        DPose expected = DPose.Compose(scene.World.PoseInOwnFrame(scene.Ball), ChildOffset.ToDPose());
        PoseChecks.AssertNear(scene.World.PoseInOwnFrame(child.Entity).Position, expected.Position, 1e-9);
        scene.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AChildSpawnedInTheTickOfItsParentsHandOffHearsNoFrameChangedBecauseItWasNeverInTheOldFrame()
    {
        using var scene = new Scene();
        long handOff = scene.RunUntilHandOff();

        scene.World.Tick();
        scene.World.Tick();

        Spawn child = scene.Children.Single(c => c.Tick == handOff);
        child.FrameChangesHeard.Should().Be(0);
        // The child of the tick before was live in space, so it followed the ball and heard about it.
        scene.Children.Single(c => c.Tick == handOff - 1).FrameChangesHeard.Should().Be(1);
    }

    [Fact]
    public void AGrandchildSpawnedInTheTickOfTheHandOffUnderAChildFromTheTickBeforeBecomesLiveInTheNewFrame()
    {
        using var scene = new Scene();
        long handOff = scene.RunUntilHandOff();

        scene.World.Tick();
        scene.World.Tick();

        // The child of the tick before the hand-off ran its first tick in the hand-off tick, and it still saw itself in space.
        Entity middle = scene.Children.Single(c => c.Tick == handOff - 1).Entity;
        Spawn grandchild = scene.Grandchildren.Should().ContainSingle(g => g.Tick == handOff).Subject;
        scene.World.Attachments.ParentOf(grandchild.Entity).Should().Be(middle);
        scene.World.GetFrame(grandchild.Entity).Should().Be(scene.Station);
        DPose expected = DPose.Compose(scene.World.PoseInOwnFrame(middle), GrandchildOffset.ToDPose());
        PoseChecks.AssertNear(scene.World.PoseInOwnFrame(grandchild.Entity).Position, expected.Position, 1e-9);
        grandchild.FrameChangesHeard.Should().Be(0);
        scene.World.Errors.Should().BeEmpty();
    }
}
