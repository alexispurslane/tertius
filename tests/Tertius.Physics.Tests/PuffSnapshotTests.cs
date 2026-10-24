using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The puffs of the render snapshot that the World publishes at step 11 (docs/plans/particles.md, "The render snapshot"
/// and "Task 6: the assembly"): the snapshot shows the rows of the puff table, placed through the frame tree relative to
/// the camera, on the real World and Jolt backend.
/// </summary>
public class PuffSnapshotTests
{
    /// <summary>The hash of the snapshot puffs of <see cref="BusySceneWithCamera"/> after 300 ticks.</summary>
    private const ulong PinnedHash = 17855250322041096263;

    /// <summary>
    /// A space without a field, a hull in it that owns a frame, turned and drifting, with a jet in the hull's frame and a
    /// glow jet in space. The camera stands in space, so the hull's puffs reach the camera through the frame tree.
    /// </summary>
    private static (Simulation.World World, Entity Camera, FrameHandle Space, FrameHandle Ship)
        JetAndCameraInTwoFrames()
    {
        World world = NewWorld(backend: out _);
        FrameHandle space =
            world.CreateRootFrame(new(FrameBounds.Sphere(1e6f), default, MediumField.Uniform(1.225f, 2)));
        Entity hull = world.Spawn(new()
        {
            Frame = space,
            Pose = new(new(40, -3, -25), DQuaternion.FromAxisAngle(new Double3(1, 2, 0.5).Normalized(), 0.7)),
            LinearVelocity = new(3, 0.5, -1),
            AngularVelocity = new(0, 0.2f, 0),
            Body = new(Shape: new BoxShapeDesc(new(2, 1, 5)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new(FrameBounds.Sphere(20))
        });
        FrameHandle ship = world.GetOwnedFrame(hull);
        PuffTesting.SpawnEmitter(world: world, frame: ship, place: new(0, 0, 6), emitter: new(Rate: 60, Speed: 4, Spread: 0.3f, Look: PuffTesting.Hot));
        PuffTesting.SpawnEmitter(world: world, frame: space, place: new(-10, 0, 0), emitter: new(Rate: 30, Speed: 2, Spread: 0.5f, Look: PuffTesting.Cold));
        Entity camera = world.Spawn(new()
        {
            Frame = space,
            Pose = new(new(5, 2, 60), DQuaternion.FromAxisAngle(new(0, 1, 0), 0.3)),
            Render = new RenderData()
        });
        world.Camera = camera;
        return (world, camera, space, ship);
    }

    private static RenderSnapshot Latest(World world)
    {
        world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot).Should().BeTrue();
        return snapshot;
    }

    [Fact]
    public void ASnapshotShowsThePuffsOfTheEmittersInTheWorld()
    {
        (World scene, Entity camera, FrameHandle space, FrameHandle ship) = JetAndCameraInTwoFrames();
        using World world = scene;

        Tick(world, 40);

        RenderSnapshot snapshot = Latest(world);
        PuffView view = world.Puffs.View;
        view.Ranges.ToArray().Select(range => range.Frame).Should()
            .BeEquivalentTo([space, ship], "both jets made puffs");
        snapshot.Puffs.Should().HaveCount(view.Rows.Length);
        snapshot.PuffLooks.Should().Equal(view.Looks.ToArray());
        snapshot.PuffAgeStep.Should().Be(world.DeltaTime, "each tick's build follows the build of the tick before");
        Double3 eye = world.PoseIn(camera, space).Position;
        foreach (PuffRange range in view.Ranges)
        {
            DPose frameInSpace = world.Frames.Relative(range.Frame, space);
            for (int i = range.Start; i < range.Start + range.Count; i++)
            {
                Puff row = view.Rows[i];
                RenderPuff puff = snapshot.Puffs[i];
                var expected = (frameInSpace.TransformPoint(row.Place) - eye).ToVector3();
                Vector3.Distance(puff.Position, expected).Should().BeLessThan(1e-4f,
                    $"puff {i} lies where its row lies, through the frame tree");
                puff.Age.Should().Be(row.Age);
                puff.Look.Should().Be(row.Look);
            }
        }

        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ASnapshotWithoutACameraShowsNoPuffs()
    {
        using World world = JetAndCameraInTwoFrames().World;
        world.Destroy(world.Camera);
        world.Camera = Entity.Null;

        Tick(world, 10);

        world.Puffs.Count.Should().BePositive();
        RenderSnapshot snapshot = Latest(world);
        snapshot.Puffs.Should().BeEmpty("a snapshot without a camera shows nothing");
        snapshot.PuffLooks.Should().BeEmpty();
    }

    /// <summary>
    /// The busy scene of the puff tests in air under gravity, with a cap that binds, seen from a camera on the ground: a
    /// ball that drops on the floor with a jet on it, a thrusting hull with a jet below it and one in its own frame, and an
    /// emitter whose look changes every tick.
    /// </summary>
    private static World BusySceneWithCamera()
    {
        World world = NewWorld(backend: out _, configure: options => options.PuffCapacity = 500);
        FrameHandle ground = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f),
            GravityField.Uniform(new(0, -9.81f, 0)),
            MediumField.Uniform(1.225f, 2)));
        Floor(world, ground);
        world.Spawn(new()
        {
            Frame = ground,
            Pose = At(0, 5),
            Body = Ball(),
            Render = new RenderData { Emitter = new(Rate: 90, Speed: 8, Spread: 0.4f, Look: PuffTesting.Hot) }
        });
        var thruster = new Thruster(1000) { Acceleration = 2, Direction = Vector3.UnitY };
        Entity hull = PuffTesting.Hull(world: world, space: ground, place: new(30, 3, 0), components: thruster);
        PuffTesting.AttachEmitter(world: world, parent: hull, offset: new(0, -2, 0), emitter: new(Rate: 120, Speed: 20, Spread: 0.2f, Look: PuffTesting.Hot));
        PuffTesting.SpawnEmitter(world: world, frame: world.GetOwnedFrame(hull), place: new(0, 1, 0), emitter: new(Rate: 30, Speed: 3, Spread: 1.5f, Look: PuffTesting.Cold));
        var changer = new SelfPilot
        {
            EveryTick = self => self.Render.Emitter = new(Rate: 60, Speed: 5, Spread: 0.1f,
                Look: PuffTesting.Cold with { TemperatureRatio = 0.5f + self.Tick / 50 * 0.25f, Life = 1.5f })
        };
        PuffTesting.SpawnEmitter(world: world, frame: ground, place: new(-20, 10, 0), emitter: default, changer);
        world.Camera = world.Spawn(new() { Frame = ground, Pose = At(10, 2, 40), Render = new RenderData() });
        return world;
    }

    [Fact]
    public void TheSameRunGivesTheSameSnapshotPuffsBitForBit()
    {
        using World first = BusySceneWithCamera();
        using World second = BusySceneWithCamera();

        Tick(first, 300);
        Tick(second, 300);

        RenderSnapshot firstSnapshot = Latest(first);
        firstSnapshot.Puffs.Should().HaveCount(500, "the cap binds in this scene");
        WorldHash.OfPuffs(Latest(second)).Should().Be(WorldHash.OfPuffs(firstSnapshot));
        // tools/test-all.sh runs this test in Debug and in Release, so both builds must give this hash.
        WorldHash.OfPuffs(firstSnapshot).Should().Be(PinnedHash);
        first.Errors.Should().BeEmpty();
    }
}
