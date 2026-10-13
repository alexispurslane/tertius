using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Physics.Tests;

/// <summary>
/// Numeric frames at vast distances (docs/plans/numeric-frames.md, "Nesting for vast distances", step 5). A numeric frame
/// far from its parent's origin moves under a resting frame on a coarse grid near it, so no position in a parent grows
/// large. Reads and the render snapshot between nearby entities compose through their lowest common ancestor, so no large
/// number enters them.
/// </summary>
public class WorldNumericNestingTests
{
    /// <summary>One light-year, in meters. A double steps by 2 m there.</summary>
    private const double LightYear = 9.4607e15;

    /// <summary>
    /// How fast the crate moves away from the pod along X, in m/s: about one millimeter a tick. A body starts in its
    /// semantic frame's own system, where Jolt keeps its velocity as a float, so the drift is a power of two that stays
    /// exact on top of the speeds of the tests.
    /// </summary>
    private const double Drift = 1.0 / 16;

    /// <summary>The grid of a resting frame that nests, in meters: 2^20 m.</summary>
    private const double Grid = 1 << 20;

    private static readonly BodyDesc PodHull = new(new BoxShapeDesc(new Vector3(2)), Mass: 1000, LinearDamping: 0, AngularDamping: 0,
        InteriorShape: new SphereShapeDesc(0.05f));

    private static readonly BodyDesc CrateBody = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A pod with its own frame and a camera in it, and a crate beside it, a light-year out in a frame without gravity.</summary>
    private sealed record Scene(World World, FrameHandle Pod, Entity Crate);

    /// <summary>
    /// The pod and the crate start a light-year from the origin of a root frame without gravity, 50 m apart along Y, and
    /// both move at <paramref name="speed"/> along X. The crate moves <see cref="Drift"/> faster, so it moves away from
    /// the pod by about a millimeter a tick. They are too far apart to touch, so each gets its own numeric frame. The
    /// camera sits at the pod's origin, and the crate is drawn.
    /// </summary>
    private static Scene PodAndCrate(double speed, NumericFrameOptions? options = null)
    {
        World world = WorldTesting.NewWorld(out _, numericFrames: options);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e18f), MotionDriver.Static, GravityField.None));
        Entity hull = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = WorldTesting.At(LightYear),
            LinearVelocity = new Double3(speed, 0, 0),
            Body = PodHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Physics),
        });
        FrameHandle pod = world.GetOwnedFrame(hull);
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = WorldTesting.At(LightYear, 50),
            LinearVelocity = new Double3(speed + Drift, 0, 0),
            Body = CrateBody,
            Render = new RenderData { Mesh = new MeshId(0), Flags = RenderFlags.Visible, BoundingRadius = 1, Scale = Vector3.One },
        });
        world.Camera = world.Spawn(new SpawnDesc { Frame = pod, Pose = DPose.Identity, Render = new RenderData() });
        return new Scene(world, pod, crate);
    }

    /// <summary>The crate's X in the pod's frame, through the World, through the Snapshot, and in the render snapshot.</summary>
    private static (double World, double Snapshot, double Render) CrateX(Scene scene)
    {
        World world = scene.World;
        world.Snapshot.TryGetPoseIn(scene.Crate, scene.Pod, out DPose inSnapshot).Should().BeTrue();
        world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot render).Should().BeTrue();
        RenderInstance instance = render.Instances.Should().ContainSingle(i => i.EntityIndex == scene.Crate.Index).Subject;
        return (world.PoseIn(scene.Crate, scene.Pod).Position.X, inSnapshot.Position.X, instance.Position.X);
    }

    /// <summary>Checks that each read of the crate moved about a millimeter along X since the previous tick.</summary>
    private static void ShouldStepByAMillimeter((double World, double Snapshot, double Render) before,
        (double World, double Snapshot, double Render) after, int tick)
    {
        double step = Drift / 60;
        (after.World - before.World).Should().BeApproximately(step, 1e-4, $"the World's read steps by a millimeter at tick {tick}");
        (after.Snapshot - before.Snapshot).Should().BeApproximately(step, 1e-4, $"the Snapshot's read steps by a millimeter at tick {tick}");
        (after.Render - before.Render).Should().BeApproximately(step, 1e-4, $"the render snapshot steps by a millimeter at tick {tick}");
    }

    [Fact]
    public void ACrateALightYearOutKeepsMillimeterStepsRelativeToAPodNextToIt()
    {
        Scene scene = PodAndCrate(speed: 1000);
        using World world = scene.World;
        world.Tick();
        var before = CrateX(scene);

        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            var after = CrateX(scene);
            ShouldStepByAMillimeter(before, after, tick);
            before = after;
        }

        world.Errors.Should().BeEmpty();
        FrameHandle crateHome = world.PhysicsHomeOf(scene.Crate);
        world.Frames.IsNumeric(crateHome).Should().BeTrue("the crate is fast, so it falls in a numeric frame");
        world.Frames.TreeLocalPose(crateHome).Position.Length.Should().BeLessThan(1e9, "its frame nests near it");
    }

    [Fact]
    public void TwoNumericFramesNearEachOtherFarFromTheOriginShareOneRestingFrameOnTheGrid()
    {
        Scene scene = PodAndCrate(speed: 1000);
        using World world = scene.World;
        world.Tick();

        FrameHandle crateHome = world.PhysicsHomeOf(scene.Crate);
        FrameHandle podHome = world.Frames.TreeParent(scene.Pod);
        crateHome.Should().NotBe(podHome, "the crate and the pod are too far apart to share a numeric frame");
        FrameHandle nest = world.Frames.TreeParent(crateHome);
        world.Frames.TreeParent(podHome).Should().Be(nest);
        world.Frames.Numeric(nest).Should().Be(NumericMode.Nesting);
        ShouldBeOnTheGrid(world.Frames.TreeLocalPose(nest).Position);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ANestingFrameJumpsByWholeGridStepsAndTheFramesBelowItKeepMillimeterSteps()
    {
        // The frames below the nest pass 2^21 m from its origin within 5 s. The nest distance is short, so the test is too.
        Scene scene = PodAndCrate(speed: 1 << 19, new NumericFrameOptions { NestDistance = 1 << 21 });
        using World world = scene.World;
        world.Tick();
        FrameHandle nest = world.Frames.TreeParent(world.PhysicsHomeOf(scene.Crate));
        Double3 start = world.Frames.TreeLocalPose(nest).Position;
        var before = CrateX(scene);

        for (int tick = 0; tick < 330; tick++)
        {
            world.Tick();
            var after = CrateX(scene);
            ShouldStepByAMillimeter(before, after, tick);
            before = after;
            FrameHandle crateHome = world.PhysicsHomeOf(scene.Crate);
            world.Frames.TreeParent(crateHome).Should().Be(nest, "the nest jumps instead of being replaced");
            ShouldBeOnTheGrid(world.Frames.TreeLocalPose(nest).Position);
            world.Frames.TreeLocalPose(crateHome).Position.Length.Should().BeLessThanOrEqualTo(1 << 21);
        }

        world.Errors.Should().BeEmpty();
        (world.Frames.TreeLocalPose(nest).Position.X - start.X).Should().BeGreaterThan(0, "the nest jumped after its frames");
    }

    private static void ShouldBeOnTheGrid(Double3 position)
    {
        (position.X % Grid).Should().Be(0);
        (position.Y % Grid).Should().Be(0);
        (position.Z % Grid).Should().Be(0);
    }
}
