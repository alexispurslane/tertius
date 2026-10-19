using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The convex radius of a box and of a convex hull, on the real World and the real Jolt backend. Jolt shrinks such a shape
/// by its convex radius and rounds it back out, so a compound of two shapes with a radius has a groove at the joint of
/// their flat faces. A ball of radius 0.25 m across such a joint of two default shapes touches the two rounded edges
/// before it touches the flat faces, so it sinks by 0.25 - 0.2458 = 4.2 mm: the two rounded edges lie 0.05 m on each side of
/// the joint and 0.05 m below the flat faces, and the ball touches both at the distance 0.25 + 0.05 from its center.
/// </summary>
public class ConvexRadiusTests
{
    private const float BallRadius = 0.25f;

    /// <summary>The height, in m, of the center of the ball when it rests on a flat face at height 0.</summary>
    private const double RestingHeight = BallRadius;

    /// <summary>The distance, in m, that the ball may be off, for a ball that rests on a flat face.</summary>
    private const double Tolerance = 1e-4;

    /// <summary>
    /// The 8 corners of a slab of 1 m along X, from <paramref name="x0"/> to <paramref name="x1"/>, 2 m along Z, and 0.5 m
    /// thick along Y, whose flat top is at height 0.
    /// </summary>
    private static Vector3[] SlabPoints(float x0, float x1) =>
    [
        new(x0, -0.5f, -1), new(x1, -0.5f, -1), new(x0, -0.5f, 1), new(x1, -0.5f, 1),
        new(x0, 0, -1), new(x1, 0, -1), new(x0, 0, 1), new(x1, 0, 1),
    ];

    private static readonly Vector3[] LeftSlab = SlabPoints(-1, 0);
    private static readonly Vector3[] RightSlab = SlabPoints(0, 1);

    private static ConvexHullShapeDesc LeftHull(float convexRadius) => new(LeftSlab, convexRadius);

    private static ConvexHullShapeDesc RightHull(float convexRadius) => new(RightSlab, convexRadius);

    /// <summary>A box of 1 m along X, 2 m along Z and 0.5 m along Y, whose flat top is at height 0, as a child of a compound.</summary>
    private static CompoundChild BoxChild(float x0, float convexRadius) =>
        new(new BoxShapeDesc(new Vector3(0.5f, 0.25f, 1), convexRadius), new Pose(new Vector3(x0 + 0.5f, -0.25f, 0), Quaternion.Identity));

    private static CompoundShapeDesc Joint(ShapeDesc left, ShapeDesc right) =>
        new([new CompoundChild(left, Pose.Identity), new CompoundChild(right, Pose.Identity)]);

    /// <summary>Spawns <paramref name="shape"/> as a static body whose origin is at (<paramref name="x"/>, 0, 0).</summary>
    private static void SpawnStatic(World world, FrameHandle ground, double x, ShapeDesc shape) =>
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(x), Body = new BodyDesc(shape, BodyMotion.Static) });

    /// <summary>Drops a ball 1 cm above its resting height onto (<paramref name="x"/>, 0, 0), lets it settle, and returns the height of its center.</summary>
    private static double RestingHeightOfBallAt(World world, FrameHandle ground, double x)
    {
        Entity ball = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(x, RestingHeight + 0.01), Body = Ball(BallRadius) });
        Tick(world, 120);
        return world.PoseInOwnFrame(ball).Position.Y;
    }

    private static double HeightOfBallAcrossTheJointOf(ShapeDesc joint)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        SpawnStatic(world, ground, 0, joint);

        double height = RestingHeightOfBallAt(world, ground, 0);

        world.Errors.Should().BeEmpty();
        return height;
    }

    [Fact]
    public void ABallAcrossTheJointOfTwoHullsOfConvexRadiusZeroRestsAtTheHeightOfTheirFlatFaces()
    {
        double height = HeightOfBallAcrossTheJointOf(Joint(LeftHull(0), RightHull(0)));

        height.Should().BeApproximately(RestingHeight, Tolerance);
    }

    [Fact]
    public void ABallAcrossTheJointOfTwoHullsWithTheDefaultConvexRadiusSinksBelowTheirFlatFaces()
    {
        // The default keeps today's shapes: the groove is the one that a 5 cm radius makes.
        double height = HeightOfBallAcrossTheJointOf(Joint(new ConvexHullShapeDesc(LeftSlab), new ConvexHullShapeDesc(RightSlab)));

        height.Should().BeApproximately(0.245804, 2 * Tolerance);
    }

    [Fact]
    public void ABallAcrossTheJointOfTwoHullsOfALargerConvexRadiusSinksDeeperThanWithTheDefault()
    {
        double height = HeightOfBallAcrossTheJointOf(Joint(LeftHull(0.1f), RightHull(0.1f)));

        // A full radius of 0.1 m would put the rounded edges 0.1 m on each side of the joint and 0.1 m below the flat
        // faces, and the ball at 0.1² + (h + 0.1)² = (0.25 + 0.1)², so h = 0.23541 m. Jolt may round a hull by less than
        // the radius that the description allows, so the ball lies between that depth and the depth of the default radius.
        height.Should().BeInRange(0.23541 - Tolerance, 0.245804 - 0.001);
    }

    [Fact]
    public void ABallAcrossTheJointOfTwoBoxesOfConvexRadiusZeroRestsAtTheHeightOfTheirFlatFaces()
    {
        double height = HeightOfBallAcrossTheJointOf(new CompoundShapeDesc([BoxChild(-1, 0), BoxChild(0, 0)]));

        height.Should().BeApproximately(RestingHeight, Tolerance);
    }

    [Fact]
    public void ABallAcrossTheJointOfTwoBoxesWithTheDefaultConvexRadiusSinksBelowTheirFlatFaces()
    {
        double height = HeightOfBallAcrossTheJointOf(new CompoundShapeDesc([BoxChild(-1, ShapeDesc.DefaultConvexRadius), BoxChild(0, ShapeDesc.DefaultConvexRadius)]));

        height.Should().BeApproximately(0.245804, 2 * Tolerance);
    }

    [Fact]
    public void ABoxOfConvexRadiusZeroHoldsARestingBallAtItsTopFace()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        // A block with the half extents (2, 0.5, 2), whose flat top is at height 0.
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, -0.5), Body = new BodyDesc(new BoxShapeDesc(new Vector3(2, 0.5f, 2), 0), BodyMotion.Static) });

        RestingHeightOfBallAt(world, ground, 0).Should().BeApproximately(RestingHeight, Tolerance);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABoxWhoseConvexRadiusIsLargerThanItsSmallestHalfExtentStillHoldsARestingBallAtItsTopFace()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        // A plate with the half extents (2, 0.1, 2) and a convex radius of 5 m: physics rounds it by 0.1 m at the most.
        var plate = new BoxShapeDesc(new Vector3(2, 0.1f, 2), 5);
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, -0.1), Body = new BodyDesc(plate, BodyMotion.Static) });

        RestingHeightOfBallAt(world, ground, 0).Should().BeApproximately(RestingHeight, Tolerance);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABoxGivesJoltTheConvexRadiusItNamesWhenItFits()
    {
        JoltRuntime.EnsureInitialized();
        using var cache = new ShapeCache();

        var box = (BoxShape)cache.Get(new BoxShapeDesc(new Vector3(2, 0.5f, 2), 0.2f));

        box.ConvexRadius.Should().Be(0.2f);
    }

    [Fact]
    public void ABoxNeverRoundsByMoreThanItsSmallestHalfExtent()
    {
        JoltRuntime.EnsureInitialized();
        using var cache = new ShapeCache();

        // Jolt requires the convex radius to fit inside the box, so a plate of 0.1 m half thickness is rounded by 0.1 m.
        var plate = (BoxShape)cache.Get(new BoxShapeDesc(new Vector3(2, 0.1f, 2), 5));

        plate.ConvexRadius.Should().Be(0.1f);
    }

    [Fact]
    public void TwoHullsThatDifferOnlyInConvexRadiusGiveDifferentCollisionResults()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        // The same point arrays, in one world and so in one shape cache. Only the radius differs.
        var roundedLeft = new ConvexHullShapeDesc(LeftSlab);
        var roundedRight = new ConvexHullShapeDesc(RightSlab);
        SpawnStatic(world, ground, -10, Joint(roundedLeft, roundedRight));
        SpawnStatic(world, ground, 10, Joint(roundedLeft with { ConvexRadius = 0 }, roundedRight with { ConvexRadius = 0 }));

        double onRounded = RestingHeightOfBallAt(world, ground, -10);
        double onSharp = RestingHeightOfBallAt(world, ground, 10);

        onSharp.Should().BeApproximately(RestingHeight, Tolerance);
        onRounded.Should().BeLessThan(onSharp - 0.003);
        world.Errors.Should().BeEmpty();
    }
}
