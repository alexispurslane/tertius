using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;
using Tertius.Physics;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The frame outlines of the render snapshot (docs/plans/blue-danube-demo.md, "Change 3: frame outlines"). With
/// <see cref="World.ShowFrameOutlines"/> on, the snapshot carries one <see cref="FrameOutline"/> for each frame that is not a
/// root, in the dense order of the frame tree. A semantic frame shows its bubble. A numeric frame shows its contents sphere:
/// the smallest sphere centred on its origin that holds the bounding sphere of each body whose physics home it is and the
/// outline sphere of each frame that hangs in it. Every center is the frame's origin relative to the render origin, in the
/// axes of the camera's frame. The tests of numeric frames run the real World on the real Jolt backend.
/// </summary>
public sealed class RenderSnapshotOutlineTests
{
    /// <summary>
    /// A speed in m/s at which a group of free bodies falls in a numeric frame: twice the 1,000 m/s of
    /// <see cref="NumericFrameOptions.CreateSpeed"/>, rounded up to a power of two.
    /// </summary>
    private const double FallingSpeed = 2048;

    /// <summary>One light-year, in meters: far beyond the 10^9 m from which a numeric frame nests.</summary>
    private const double LightYear = 9.4607e15;

    private static readonly DQuaternion QuarterTurnAboutY = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);

    /// <summary>The hull of a ship, 10 m by 6 m by 30 m: its bounding sphere has a radius of 16.4 m.</summary>
    private static readonly BodyDesc ShipHull = new(
        Shape: new BoxShapeDesc(new(5, 3, 15)),
        Mass: 1000,
        LinearDamping: 0,
        AngularDamping: 0
    );

    private static readonly BodyDesc CrateBody = new(
        Shape: new BoxShapeDesc(new(0.5f)),
        Mass: 10,
        LinearDamping: 0,
        AngularDamping: 0
    );

    private static TestWorld OnJolt(int maxDegreeOfParallelism = 1) =>
        new(
            physics: new JoltPhysicsBackend(new() { WorkerThreads = 2 }),
            maxDegreeOfParallelism: maxDegreeOfParallelism,
            configure: options => options.DisposePhysics = true
        );

    private static List<SemanticOutline> Semantic(IEnumerable<FrameOutline> outlines) =>
        [
            .. outlines
                .Select(o => o.TryGetValue(out SemanticOutline s) ? (SemanticOutline?)s : null)
                .OfType<SemanticOutline>(),
        ];

    private static List<FallingOutline> Falling(IEnumerable<FrameOutline> outlines) =>
        [
            .. outlines
                .Select(o => o.TryGetValue(out FallingOutline s) ? (FallingOutline?)s : null)
                .OfType<FallingOutline>(),
        ];

    private static List<NestingOutline> Nesting(IEnumerable<FrameOutline> outlines) =>
        [
            .. outlines
                .Select(o => o.TryGetValue(out NestingOutline s) ? (NestingOutline?)s : null)
                .OfType<NestingOutline>(),
        ];

    private static List<FollowingOutline> Following(IEnumerable<FrameOutline> outlines) =>
        [
            .. outlines
                .Select(o => o.TryGetValue(out FollowingOutline s) ? (FollowingOutline?)s : null)
                .OfType<FollowingOutline>(),
        ];

    /// <summary>The kind of frame that an outline shows, and its center.</summary>
    private static (string Kind, Vector3 Center) KindAndCenter(FrameOutline outline) =>
        outline switch
        {
            SemanticOutline { Center: var center } => ("semantic", center),
            FallingOutline { Center: var center } => ("falling", center),
            RestingOutline { Center: var center } => ("resting", center),
            NestingOutline { Center: var center } => ("nesting", center),
            FollowingOutline { Center: var center } => ("following", center),
            null => ("none", default),
        };

    private static string KindOf(World world, FrameHandle frame) =>
        world.Frames.Numeric(frame) switch
        {
            NumericMode.None => "semantic",
            NumericMode.Falling => "falling",
            NumericMode.Resting => "resting",
            NumericMode.Nesting => "nesting",
            NumericMode.Following => "following",
        };

    // ---- The view setting ----------------------------------------------------------------------------------------

    [Fact]
    public void TheSnapshotCarriesOutlinesOnlyWhileTheSettingIsOn()
    {
        using var test = new TestWorld();
        test.SpawnStaticFrame(TestWorld.At(100), radius: 20);
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.World.ShowFrameOutlines.Should().BeFalse("outlines are off by default");
        test.Run(1);
        test.World.Latest().Outlines.Should().BeEmpty("outlines are off");

        test.World.ShowFrameOutlines = true;
        test.World.PublishRenderSnapshot();
        test.World.Latest()
            .Outlines.Should()
            .ContainSingle("the first snapshot built after the change shows the cabin's outline");

        test.World.ShowFrameOutlines = false;
        test.Run(1);
        test.World.Latest().Outlines.Should().BeEmpty("outlines are off again");
    }

    // ---- Semantic frames and roots -------------------------------------------------------------------------------

    /// <summary>
    /// The camera rides at the origin of a cabin 1 km out along X, turned a quarter turn about Y, so the axes of the camera's
    /// frame are not the root's. A buoy hangs 200 m along the root's -Z from the cabin, not turned, with an ellipsoid bubble.
    /// In the cabin's axes, the root's -Z is +X, and the buoy is turned a quarter turn back about Y.
    /// </summary>
    [Fact]
    public void ASemanticFrameHasOneOutlineAtItsOriginInItsAxesWithTheRadiiOfItsBubble()
    {
        using var test = new TestWorld();
        FrameHandle cabin = test.SpawnStaticFrame(new(new(1000, 0, 0), QuarterTurnAboutY), radius: 20);
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(1000, 0, -200),
                AsFrame = new(new(new(30, 40, 50))),
            }
        );
        test.World.Camera = test.SpawnCamera(cabin, DPose.Identity);
        test.World.ShowFrameOutlines = true;

        test.Run(1);

        List<SemanticOutline> outlines = Semantic(test.World.Latest().Outlines);
        outlines.Should().HaveCount(2, "the cabin and the buoy have a parent, and the root has no outline");
        SemanticOutline cabinOutline = outlines[0],
            buoyOutline = outlines[1];
        cabinOutline.Center.Length().Should().BeLessThan(1e-4f, "the camera sits at the cabin's origin");
        Math.Abs(Quaternion.Dot(cabinOutline.Rotation, Quaternion.Identity))
            .Should()
            .BeApproximately(1, 1e-6f, "the cabin is the camera's frame");
        cabinOutline.Radii.Should().Be(new Vector3(20));
        (buoyOutline.Center - new Vector3(200, 0, 0))
            .Length()
            .Should()
            .BeLessThan(1e-3f, $"the buoy is 200 m along the cabin's +X, not at {buoyOutline.Center}");
        var back = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
        Math.Abs(Quaternion.Dot(buoyOutline.Rotation, back))
            .Should()
            .BeApproximately(1, 1e-6f, $"the buoy's axes are turned back a quarter turn, not {buoyOutline.Rotation}");
        buoyOutline.Radii.Should().Be(new Vector3(30, 40, 50));
    }

    [Fact]
    public void ARootFrameHasNoOutlineWhetherItIsTheCamerasFrameOrTheRootOfAnotherTree()
    {
        using var test = new TestWorld();
        test.SpawnStaticFrame(TestWorld.At(100), radius: 20);
        test.World.CreateRootFrame(new(FrameBounds.Sphere(5e5f)));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.ShowFrameOutlines = true;

        test.Run(1);

        test.World.Latest()
            .Outlines.Should()
            .Equal([new SemanticOutline(new(100, 0, 0), Quaternion.Identity, new(20))], "only the cabin has a parent");
    }

    /// <summary>
    /// The camera sits 50 m along +Z from the origin of the test's root. A second root lies 1 km along +X from it under the
    /// shared virtual root, turned a quarter turn about Y, and a buoy hangs 200 m along that root's -Z, with an ellipsoid
    /// bubble. The quarter turn takes the root's -Z to the first root's -X, so the buoy sits at (800, 0, 0) in the first
    /// root, and at (800, 0, -50) from the camera. Its axes are the second root's: a quarter turn about Y.
    /// </summary>
    [Fact]
    public void AFrameOfAnotherTreeHasAnOutlinePlacedThroughTheSharedRoot()
    {
        using var test = new TestWorld();
        FrameHandle other = test.World.GetOwnedFrame(
            test.World.Spawn(
                new() { Pose = new(new(1000, 0, 0), QuarterTurnAboutY), AsFrame = new(FrameBounds.Sphere(5e5f)) }
            )
        );
        test.World.Spawn(
            new()
            {
                Frame = other,
                Pose = TestWorld.At(0, 0, -200),
                AsFrame = new(new(new(30, 40, 50))),
            }
        );
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 50));
        test.World.ShowFrameOutlines = true;

        test.Run(1);

        FrameOutline outline = test
            .World.Latest()
            .Outlines.Should()
            .ContainSingle("the buoy is the one frame with a parent")
            .Subject;
        outline.TryGetValue(out SemanticOutline buoy).Should().BeTrue($"the buoy is a semantic frame, not {outline}");
        (buoy.Center - new Vector3(800, 0, -50))
            .Length()
            .Should()
            .BeLessThan(1e-3f, $"the buoy is 800 m along +X and 50 m along -Z from the camera, not at {buoy.Center}");
        var quarterTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        Math.Abs(Quaternion.Dot(buoy.Rotation, quarterTurn))
            .Should()
            .BeApproximately(1, 1e-6f, $"the buoy has the second root's axes, not {buoy.Rotation}");
        buoy.Radii.Should().Be(new Vector3(30, 40, 50));
    }

    // ---- Numeric frames ------------------------------------------------------------------------------------------

    /// <summary>
    /// A ship coasts at 2,048 m/s through the root, which has no gravity, so its hull falls in a numeric frame, and its own
    /// frame hangs in that falling frame. The ship's bubble of 50 m reaches farther than the hull's bounding sphere of
    /// 16.4 m, so the bubble decides the falling frame's sphere.
    /// </summary>
    [Fact]
    public void AShipThatFallsFastGetsAFallingOutlineCentredOnItsFrameThatHoldsItsBubble()
    {
        using TestWorld test = OnJolt();
        FrameHandle ship = ShipFallingFast(test);

        test.Run(2);

        FrameHandle home = test.World.Frames.TreeParent(ship);
        test.World.Frames.Numeric(home)
            .Should()
            .Be(NumericMode.Falling, "the ship is fast, so it falls in a numeric frame");
        List<FrameOutline> outlines = test.World.Latest().Outlines;
        FallingOutline outline = Falling(outlines).Should().ContainSingle().Subject;
        var origin = test.World.Frames.Relative(home, test.Root).Position.ToVector3();
        (outline.Center - origin).Length().Should().BeLessThan(1e-3f, "the camera sits at the root's origin");
        double reach = test.World.Frames.TreeLocalPose(ship).Position.Length + 50;
        outline
            .Radius.Should()
            .BeApproximately((float)reach, 1e-3f, "the sphere reaches the far side of the ship's bubble");
        SemanticOutline shipOutline = Semantic(outlines).Should().ContainSingle().Subject;
        ((shipOutline.Center - outline.Center).Length() + 50)
            .Should()
            .BeLessThanOrEqualTo(outline.Radius + 1e-3f, "the sphere holds the bubble");
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The ship of <see cref="ShipFallingFast"/>, with a crate at rest in the ship's frame, 49.5 m along its +Y. The crate's bounding sphere
    /// of √0.75 m reaches out of the ship's bubble of 50 m. The ship's frame is semantic, so its outline sphere is its bubble,
    /// and the crate does not grow the falling frame's sphere.
    /// </summary>
    [Fact]
    public void ABodyThatReachesOutOfASemanticFramesBubbleDoesNotGrowTheNumericFrameAroundIt()
    {
        using TestWorld test = OnJolt();
        FrameHandle ship = ShipFallingFast(test);
        Entity crate = test.World.Spawn(
            new()
            {
                Frame = ship,
                Pose = TestWorld.At(0, 49.5),
                Body = CrateBody,
            }
        );

        test.Run(2);

        FrameHandle home = test.World.Frames.TreeParent(ship);
        test.World.Frames.Numeric(home)
            .Should()
            .Be(NumericMode.Falling, "the ship is fast, so it falls in a numeric frame");
        test.World.PhysicsHomeOf(crate).Should().Be(ship, "the crate rests in the ship's frame");
        FallingOutline outline = Falling(test.World.Latest().Outlines).Should().ContainSingle().Subject;
        double reach = test.World.Frames.TreeLocalPose(ship).Position.Length + 50;
        outline
            .Radius.Should()
            .BeApproximately(
                (float)reach,
                1e-3f,
                "the sphere reaches the far side of the ship's bubble, not of the crate"
            );
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The ship of <see cref="ShipFallingFast"/>, with a cabin in the ship's frame, 45 m along its +Y, whose bubble of 20 m reaches 65 m from
    /// the ship's origin, out of the ship's bubble of 50 m. The ship's frame is semantic, so its outline sphere is its bubble,
    /// and the cabin does not grow the falling frame's sphere.
    /// </summary>
    [Fact]
    public void AFrameThatReachesOutOfASemanticFramesBubbleDoesNotGrowTheNumericFrameAroundIt()
    {
        using TestWorld test = OnJolt();
        FrameHandle ship = ShipFallingFast(test);
        FrameHandle cabin = test.SpawnStaticFrame(TestWorld.At(0, 45), radius: 20, parent: ship);

        test.Run(2);

        FrameHandle home = test.World.Frames.TreeParent(ship);
        test.World.Frames.Numeric(home)
            .Should()
            .Be(NumericMode.Falling, "the ship is fast, so it falls in a numeric frame");
        test.World.Frames.TreeParent(cabin).Should().Be(ship, "the cabin hangs in the ship's frame");
        FallingOutline outline = Falling(test.World.Latest().Outlines).Should().ContainSingle().Subject;
        double reach = test.World.Frames.TreeLocalPose(ship).Position.Length + 50;
        outline
            .Radius.Should()
            .BeApproximately(
                (float)reach,
                1e-3f,
                "the sphere reaches the far side of the ship's bubble, not of the cabin's"
            );
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ship that coasts at 2,048 m/s through the test's root, which has no gravity, with a frame of its own whose bubble
    /// has a radius of 50 m. The camera sits at the root's origin, and the outlines are on.
    /// </summary>
    private static FrameHandle ShipFallingFast(TestWorld test)
    {
        Entity hull = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                LinearVelocity = new(FallingSpeed, 0, 0),
                Body = ShipHull,
                AsFrame = new(FrameBounds.Sphere(50)),
            }
        );
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.ShowFrameOutlines = true;
        return test.World.GetOwnedFrame(hull);
    }

    /// <summary>
    /// A station's kinematic hull goes round the asteroid at 2 km/s on a Kepler orbit, so it lives in a following frame of its
    /// own, and the station's frame hangs at that frame's origin. Its bubble of 30 m reaches farther than the hull's bounding
    /// sphere of 3.5 m.
    /// </summary>
    [Fact]
    public void AStationOnItsOrbitGetsAnOrbitOutlineCentredOnItsFollowingFrameThatHoldsItsBubble()
    {
        using TestWorld test = OnJolt();
        FrameHandle space = Scenes.AsteroidSpace(test.World);
        Entity hull = test.World.Spawn(
            new SpawnDesc
            {
                Frame = space,
                Body = new(Shape: new BoxShapeDesc(new(2)), Motion: BodyMotion.Kinematic),
                AsFrame = new(FrameBounds.Sphere(30)),
            }.OnKeplerOrbit(test.World, KeplerOrbit.Circular(5_000))
        );
        FrameHandle station = test.World.GetOwnedFrame(hull);
        Entity camera = test.SpawnCamera(space, TestWorld.At(4_000));
        test.World.Camera = camera;
        test.World.ShowFrameOutlines = true;

        test.Run(3);

        FrameHandle orbit = test.World.Frames.TreeParent(station);
        test.World.Frames.Numeric(orbit)
            .Should()
            .Be(NumericMode.Following, "the station's hull moves fast on its rails");
        FollowingOutline following = Following(test.World.Latest().Outlines).Should().ContainSingle().Subject;
        var origin = (test.World.Frames.Relative(orbit, space).Position - new Double3(4_000, 0, 0)).ToVector3();
        (following.Center - origin)
            .Length()
            .Should()
            .BeLessThan(1e-3f, "the center is the following frame's origin relative to the camera");
        following
            .Radius.Should()
            .BeApproximately(
                30,
                1e-3f,
                "the station hangs at the following frame's origin, and its bubble reaches 30 m"
            );
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A static floor of 40 m by 1 m by 4 m lies at rest 10,000 km out along X in the root, which has no gravity, beyond the
    /// 2^23 m from which a slow group rests in a numeric frame. A crate floats at rest 0.6 m above the floor's top, near its
    /// far end. The two can touch, so they form one group, which gets the center of its static body: the floor's center. So
    /// the floor sits at the resting frame's origin, and the crate at (19.5, 1.6, 1.5) from it. The crate's far corner, at
    /// √385.06 + √0.75 = 20.48897 m, reaches farther than the floor's bounding sphere of √404.25 = 20.10597 m.
    /// </summary>
    [Fact]
    public void ASlowGroupFarOutGetsARestingOutlineWhoseRadiusIsTheFarthestPointOfItsBodies()
    {
        using TestWorld test = OnJolt();
        Entity floor = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(1e7),
                Body = new(Shape: new BoxShapeDesc(new(20, 0.5f, 2)), Motion: BodyMotion.Static),
            }
        );
        Entity crate = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(1e7 + 19.5, 1.6, 1.5),
                Body = CrateBody,
            }
        );
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(1e7 - 100));
        test.World.ShowFrameOutlines = true;

        test.Run(2);

        FrameHandle home = test.World.PhysicsHomeOf(floor);
        test.World.Frames.Numeric(home)
            .Should()
            .Be(NumericMode.Resting, "a slow group 10,000 km out rests in a numeric frame");
        test.World.PhysicsHomeOf(crate).Should().Be(home, "the crate and the floor form one group");
        FrameOutline outline = test.World.Latest().Outlines.Should().ContainSingle().Subject;
        outline
            .TryGetValue(out RestingOutline resting)
            .Should()
            .BeTrue($"the one outline is the resting frame's, not {outline}");
        resting
            .Radius.Should()
            .BeApproximately(20.48897f, 1e-4f, "the crate's far corner is the farthest point of the group");
        (resting.Center - new Vector3(100, 0, 0))
            .Length()
            .Should()
            .BeLessThan(1e-3f, "the floor's center is 100 m along +X from the camera");
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The floor and the crate of the test above. Once the crate is gone, the resting frame holds the floor alone, so the
    /// next snapshot's sphere shrinks to the floor's bounding sphere of √404.25 = 20.10597 m: each build measures what the
    /// frame holds at that build, not the largest sphere of any build before it.
    /// </summary>
    [Fact]
    public void AContentsSphereShrinksInTheNextSnapshotWhenItsFarthestBodyGoes()
    {
        using TestWorld test = OnJolt();
        Entity floor = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(1e7),
                Body = new(Shape: new BoxShapeDesc(new(20, 0.5f, 2)), Motion: BodyMotion.Static),
            }
        );
        Entity crate = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(1e7 + 19.5, 1.6, 1.5),
                Body = CrateBody,
            }
        );
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(1e7 - 100));
        test.World.ShowFrameOutlines = true;
        test.Run(2);
        test.World.Latest()
            .Outlines.Should()
            .ContainSingle()
            .Which.TryGetValue(out RestingOutline before)
            .Should()
            .BeTrue();
        before.Radius.Should().BeApproximately(20.48897f, 1e-4f, "the crate's far corner reaches farthest");

        test.World.Destroy(crate);
        test.Run(1);

        test.World.Frames.Numeric(test.World.PhysicsHomeOf(floor))
            .Should()
            .Be(NumericMode.Resting, "the floor still rests far out");
        test.World.Latest()
            .Outlines.Should()
            .ContainSingle()
            .Which.TryGetValue(out RestingOutline after)
            .Should()
            .BeTrue();
        after.Radius.Should().BeApproximately(20.10597f, 1e-4f, "the floor alone is left");
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The pod and the crate of <see cref="PodAndCrateALightYearOut"/> each fall in a numeric frame of their own, and both of
    /// those frames hang in one nesting frame on the grid. The nesting frame's sphere reaches the far side of the farther
    /// one's sphere.
    /// </summary>
    [Fact]
    public void FallingFramesALightYearOutGetANestingOutlineThatHoldsTheirOutlines()
    {
        using TestWorld test = OnJolt();
        (FrameHandle pod, Entity crate) = PodAndCrateALightYearOut(test.World);

        test.Run(1);

        FrameHandle podHome = test.World.Frames.TreeParent(pod),
            crateHome = test.World.PhysicsHomeOf(crate);
        FrameHandle nest = test.World.Frames.TreeParent(crateHome);
        test.World.Frames.TreeParent(podHome).Should().Be(nest, "both falling frames nest in one frame");
        test.World.Frames.Numeric(nest).Should().Be(NumericMode.Nesting);
        List<FrameOutline> outlines = test.World.Latest().Outlines;
        NestingOutline nesting = Nesting(outlines).Should().ContainSingle().Subject;
        List<FallingOutline> falling = Falling(outlines);
        falling.Should().HaveCount(2);
        // The nesting frame lies on a grid of 2^20 m, so its frames can sit 500 km from its origin, where a float steps by 3 cm.
        double reach = Math.Max(
            test.World.Frames.TreeLocalPose(podHome).Position.Length + 20,
            test.World.Frames.TreeLocalPose(crateHome).Position.Length + Math.Sqrt(0.75)
        );
        nesting
            .Radius.Should()
            .BeApproximately((float)reach, 0.1f, "the sphere reaches the far side of the pod's bubble or the crate");
        foreach (FallingOutline inside in falling)
            ((inside.Center - nesting.Center).Length() + inside.Radius)
                .Should()
                .BeLessThanOrEqualTo(nesting.Radius + 0.1f, "the sphere holds each falling frame's sphere");
        test.World.Errors.Should().BeEmpty();
    }

    // ---- Order -------------------------------------------------------------------------------------------------

    [Fact]
    public void TheOutlinesComeInTheDenseOrderOfTheFrameTree()
    {
        using TestWorld test = OnJolt();
        (FrameHandle pod, _) = PodAndCrateALightYearOut(test.World);

        test.Run(2);

        // The pod went under its falling frame, which the numeric update made after the pod, so the dense order is not
        // the order of creation.
        var expected = new List<(string Kind, Vector3 Center)>();
        for (int i = 0; i < test.World.Frames.Count; i++)
        {
            FrameHandle frame = test.World.Frames.AtDense(i);
            if (!test.World.Frames.TreeParent(frame).IsNull)
                expected.Add((KindOf(test.World, frame), test.World.Frames.Relative(frame, pod).Position.ToVector3()));
        }

        List<(string Kind, Vector3 Center)> actual = [.. test.World.Latest().Outlines.Select(KindAndCenter)];
        actual.Select(o => o.Kind).Should().Equal(expected.Select(o => o.Kind));
        for (int i = 0; i < actual.Count; i++)
            (actual[i].Center - expected[i].Center)
                .Length()
                .Should()
                .BeLessThan(
                    0.1f,
                    $"outline {i}, a {actual[i].Kind} outline, is the frame at its place in the dense order"
                );
    }

    [Fact]
    public void TheOutlinesAreTheSameInTwoRunsAndOnTheWorkersAsOnOneThread()
    {
        List<FrameOutline> first = OutlinesAfterFiveTicks(maxDegreeOfParallelism: 1, parallelThreshold: null);
        List<FrameOutline> second = OutlinesAfterFiveTicks(maxDegreeOfParallelism: 1, parallelThreshold: null);
        List<FrameOutline> onWorkers = OutlinesAfterFiveTicks(maxDegreeOfParallelism: 4, parallelThreshold: 1);

        first.Should().HaveCount(4, "the pod's frame, the two falling frames and the nesting frame");
        second.Should().Equal(first);
        onWorkers.Should().Equal(first);
    }

    private static List<FrameOutline> OutlinesAfterFiveTicks(int maxDegreeOfParallelism, int? parallelThreshold)
    {
        using TestWorld test = OnJolt(maxDegreeOfParallelism);
        if (parallelThreshold is { } threshold)
            Inside.RenderBuilderOf(test.World).ParallelThreshold = threshold;
        PodAndCrateALightYearOut(test.World);
        test.Run(5);
        return [.. test.World.Latest().Outlines];
    }

    /// <summary>
    /// A pod with a frame of its own, whose bubble has a radius of 20 m, and a crate beside it, a light-year out along X in a
    /// root frame without gravity, 50 m apart along Y. Both move at 2,048 m/s along X, and the crate 1/16 m/s faster. They
    /// are too far apart to touch, so each falls in a numeric frame of its own, and both frames nest. The camera sits at the
    /// pod's origin, and the outlines are on.
    /// </summary>
    private static (FrameHandle Pod, Entity Crate) PodAndCrateALightYearOut(World world)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e18f), GravityField.None));
        Entity hull = world.Spawn(
            new()
            {
                Frame = space,
                Pose = TestWorld.At(LightYear),
                LinearVelocity = new(FallingSpeed, 0, 0),
                Body = new(
                    Shape: new BoxShapeDesc(new(2)),
                    Mass: 1000,
                    LinearDamping: 0,
                    AngularDamping: 0,
                    InteriorShape: new SphereShapeDesc(0.05f)
                ),
                AsFrame = new(FrameBounds.Sphere(20)),
            }
        );
        FrameHandle pod = world.GetOwnedFrame(hull);
        Entity crate = world.Spawn(
            new()
            {
                Frame = space,
                Pose = TestWorld.At(LightYear, 50),
                LinearVelocity = new(FallingSpeed + 1.0 / 16, 0, 0),
                Body = CrateBody,
            }
        );
        world.Camera = world.Spawn(
            new()
            {
                Frame = pod,
                Pose = DPose.Identity,
                Render = new RenderData(),
            }
        );
        world.ShowFrameOutlines = true;
        return (pod, crate);
    }
}
