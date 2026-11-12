using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The puffs of the render snapshot that the World publishes at step 11 (docs/plans/particles.md, "The render snapshot"
/// and "Task 6: the assembly"): the snapshot shows the rows of the puff table, placed through the frame tree relative to
/// the camera, on the real World and Jolt backend.
/// </summary>
public sealed class PuffSnapshotTests
{
    /// <summary>The hash of the snapshot puffs of <see cref="PuffTesting.BusySceneWithCamera"/> after 300 ticks, on arm64 (<see cref="RecordedReference"/>).</summary>
    private const ulong PinnedHash = 2111527910242662902;

    /// <summary>
    /// A space without a field, a hull in it that owns a frame, turned and drifting, with a jet in the hull's frame and a
    /// glow jet in space. The camera stands in space, so the hull's puffs reach the camera through the frame tree.
    /// </summary>
    private static (World World, Entity Camera, FrameHandle Space, FrameHandle Ship) JetAndCameraInTwoFrames()
    {
        World world = NewWorld(backend: out _);
        FrameHandle space = world.CreateRootFrame(
            new(FrameBounds.Sphere(1e6f), default, MediumField.Uniform(1.225f, 2))
        );
        Entity hull = world.Spawn(
            new()
            {
                Frame = space,
                Pose = new(new(40, -3, -25), DQuaternion.FromAxisAngle(new Double3(1, 2, 0.5).Normalized(), 0.7)),
                LinearVelocity = new(3, 0.5, -1),
                AngularVelocity = new(0, 0.2f, 0),
                Body = new(Shape: new BoxShapeDesc(new(2, 1, 5)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
                AsFrame = new(FrameBounds.Sphere(20)),
            }
        );
        FrameHandle ship = world.GetOwnedFrame(hull);
        PuffTesting.SpawnEmitter(
            world: world,
            frame: ship,
            place: new(0, 0, 6),
            emitter: new(Rate: 60, Speed: 4, Spread: 0.3f, Look: PuffTesting.Hot)
        );
        PuffTesting.SpawnEmitter(
            world: world,
            frame: space,
            place: new(-10, 0, 0),
            emitter: new(Rate: 30, Speed: 2, Spread: 0.5f, Look: PuffTesting.Cold)
        );
        Entity camera = world.Spawn(
            new()
            {
                Frame = space,
                Pose = new(new(5, 2, 60), DQuaternion.FromAxisAngle(new(0, 1, 0), 0.3)),
                Render = new RenderData(),
            }
        );
        world.Camera = camera;
        return (world, camera, space, ship);
    }

    [Fact]
    public void ASnapshotShowsThePuffsOfTheEmittersInTheWorld()
    {
        (World scene, Entity camera, FrameHandle space, FrameHandle ship) = JetAndCameraInTwoFrames();
        using World world = scene;

        Tick(world, 40);

        RenderSnapshot snapshot = PuffTesting.Latest(world);
        PuffView view = world.Puffs.View;
        view.Ranges.ToArray()
            .Select(range => range.Frame)
            .Should()
            .BeEquivalentTo([space, ship], "both jets made puffs");
        snapshot.Puffs.Should().HaveCount(view.Rows.Length);
        snapshot.PuffLooks.Should().Equal(view.Looks.ToArray());
        snapshot
            .PuffAgeStep.Should()
            .Be((float)world.DeltaTime, "each tick's build follows the build of the tick before");
        Double3 eye = world.PoseIn(camera, space).Position;
        foreach (PuffRange range in view.Ranges)
        {
            DPose frameInSpace = world.Frames.Relative(range.Frame, space);
            for (int i = range.Start; i < range.Start + range.Count; i++)
            {
                Puff row = view.Rows[i];
                RenderPuff puff = snapshot.Puffs[i];
                var expected = (frameInSpace.TransformPoint(row.Place) - eye).ToVector3();
                Vector3
                    .Distance(puff.Position, expected)
                    .Should()
                    .BeLessThan(1e-4f, $"puff {i} lies where its row lies, through the frame tree");
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
        RenderSnapshot snapshot = PuffTesting.Latest(world);
        snapshot.Puffs.Should().BeEmpty("a snapshot without a camera shows nothing");
        snapshot.PuffLooks.Should().BeEmpty();
    }

    [Fact]
    public void TheSameRunGivesTheSameSnapshotPuffsBitForBit()
    {
        using World first = PuffTesting.BusySceneWithCamera();
        using World second = PuffTesting.BusySceneWithCamera();

        Tick(first, 300);
        Tick(second, 300);

        RenderSnapshot firstSnapshot = PuffTesting.Latest(first);
        firstSnapshot.Puffs.Should().HaveCount(500, "the cap binds in this scene");
        WorldHash.OfPuffs(PuffTesting.Latest(second)).Should().Be(WorldHash.OfPuffs(firstSnapshot));
        first.Errors.Should().BeEmpty();
        Assert.SkipUnless(RecordedReference.Applies, RecordedReference.SkipReason);
        // tools/test-all.sh runs this test in Debug and in Release, so both builds must give this hash.
        WorldHash.OfPuffs(firstSnapshot).Should().Be(PinnedHash);
    }
}
