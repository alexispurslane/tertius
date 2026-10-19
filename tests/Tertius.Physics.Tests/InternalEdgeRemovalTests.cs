using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Which Jolt bodies remove the internal edges of their contacts (ter-fb3w): every body whose own shape is a surface
/// that sharp pieces tile sets Jolt's <c>EnhancedInternalEdgeRemoval</c>, and every other body leaves it off. Such a
/// shape is a mesh, or a compound whose every child is a box or a convex hull of convex radius 0, a mesh, or such a
/// compound. That holds for each Jolt body of an entity by its own shape: the body itself, its ghosts, and the interior
/// of the frame that it owns. What the flag does is in the host's RevolvedSolidJointTests: it stops a ball that lands
/// near a joint from being pushed sideways, and a box that slides across joints from tripping. Why a compound with a
/// rounded child leaves it off is in ConvexRadiusTests: the groove at its joints is real, and the ball rests in it.
/// </summary>
/// <remarks>
/// These tests read the flag from Jolt, because no contact shows it on a convex body. Jolt removes the internal edges of
/// a pair when either body has the flag, so against a mesh or a compound that has it, the convex body's own flag changes
/// nothing. A pair of two convex bodies gives one contact, and Jolt keeps the deepest contact of a pair unchanged, so the
/// flag changes nothing there either. It only costs time.
/// </remarks>
public class InternalEdgeRemovalTests
{
    private static readonly MeshShapeDesc Triangle = new([new(-5, 0, -5), new(5, 0, -5), new(0, 0, 5)], [0, 2, 1]);

    private static readonly BoxShapeDesc SharpBox = new(new Vector3(1), ConvexRadius: 0);

    /// <summary>Two boxes of convex radius 0 side by side along X, whose faces meet flat at x = 0.</summary>
    private static readonly CompoundShapeDesc TwoSharpBoxes = new(
    [
        new CompoundChild(SharpBox, new Pose(new Vector3(-1, 0, 0), Quaternion.Identity)),
        new CompoundChild(SharpBox, new Pose(new Vector3(1, 0, 0), Quaternion.Identity)),
    ]);

    private static CompoundShapeDesc Beside(ShapeDesc first, ShapeDesc second) => new(
    [
        new CompoundChild(first, new Pose(new Vector3(-1, 0, 0), Quaternion.Identity)),
        new CompoundChild(second, new Pose(new Vector3(1, 0, 0), Quaternion.Identity)),
    ]);

    private static readonly Vector3[] Tetrahedron = [new(1, 0, 0), new(-1, 0, 0), new(0, 2, 0), new(0, 0, -3)];

    public static TheoryData<string, bool> Shapes() => new()
    {
        { "box", false },
        { "box of convex radius 0", false },
        { "sphere", false },
        { "capsule", false },
        { "convex hull", false },
        { "mesh", true },
        { "compound of boxes of convex radius 0", true },
        { "compound of hulls of convex radius 0", true },
        { "compound that holds a mesh", true },
        { "compound of a compound of boxes of convex radius 0", true },
        { "compound of default boxes", false },
        { "compound of default hulls", false },
        { "compound of a box of convex radius 0 and a default box", false },
        { "compound of a box of convex radius 0 and a sphere", false },
        { "compound of a box of convex radius 0 and a capsule", false },
        { "compound of a box of convex radius 0 and a compound of default boxes", false },
    };

    private static ShapeDesc Shape(string name) => name switch
    {
        "box" => new BoxShapeDesc(new Vector3(1)),
        "box of convex radius 0" => SharpBox,
        "sphere" => new SphereShapeDesc(1),
        "capsule" => new CapsuleShapeDesc(1, 0.5f),
        "convex hull" => new ConvexHullShapeDesc(Tetrahedron),
        "mesh" => Triangle,
        "compound of boxes of convex radius 0" => TwoSharpBoxes,
        "compound of hulls of convex radius 0" => Beside(new ConvexHullShapeDesc(Tetrahedron, 0), new ConvexHullShapeDesc(Tetrahedron, 0)),
        "compound that holds a mesh" => new CompoundShapeDesc([new CompoundChild(Triangle, new Pose(Vector3.Zero, Quaternion.Identity))]),
        "compound of a compound of boxes of convex radius 0" => new CompoundShapeDesc([new CompoundChild(TwoSharpBoxes, Pose.Identity)]),
        "compound of default boxes" => Beside(new BoxShapeDesc(new Vector3(1)), new BoxShapeDesc(new Vector3(1))),
        "compound of default hulls" => Beside(new ConvexHullShapeDesc(Tetrahedron), new ConvexHullShapeDesc(Tetrahedron)),
        "compound of a box of convex radius 0 and a default box" => Beside(SharpBox, new BoxShapeDesc(new Vector3(1))),
        "compound of a box of convex radius 0 and a sphere" => Beside(SharpBox, new SphereShapeDesc(1)),
        "compound of a box of convex radius 0 and a capsule" => Beside(SharpBox, new CapsuleShapeDesc(1, 0.5f)),
        "compound of a box of convex radius 0 and a compound of default boxes" =>
            Beside(SharpBox, Beside(new BoxShapeDesc(new Vector3(1)), new BoxShapeDesc(new Vector3(1)))),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>The flag of the Jolt body of <paramref name="kind"/> that stands for <paramref name="entity"/> in the system of <paramref name="frame"/>.</summary>
    private static bool RemovesInternalEdges(JoltPhysicsBackend backend, FrameHandle frame, Entity entity, BodyKind kind) =>
        backend.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == kind && BodyUserData.Names(facts.UserData, entity)).EnhancedInternalEdgeRemoval;

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ABodyRemovesInternalEdgesExactlyWhenSharpPiecesTileItsShape(string shape, bool removes)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        // A mesh is only allowed on a static body.
        BodyMotion motion = Shape(shape).ContainsMesh() ? BodyMotion.Static : BodyMotion.Dynamic;
        Entity body = world.Spawn(new SpawnDesc { Frame = space, Body = new BodyDesc(Shape(shape), motion) });
        world.Tick();

        RemovesInternalEdges(backend, space, body, BodyKind.Primary).Should().Be(removes);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Two ships at rest 50 m apart, each a hull with a bubble of 30 m, so that each has a ghost in the other's frame: one
    /// hull a compound of two boxes of convex radius 0, the other a box.
    /// </summary>
    [Fact]
    public void AGhostRemovesInternalEdgesExactlyWhenSharpPiecesTileItsShape()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
        Entity compound = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(TwoSharpBoxes, Mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30)),
        });
        Entity box = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(50),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30)),
        });
        Tick(world, 3);

        RemovesInternalEdges(backend, world.GetOwnedFrame(box), compound, BodyKind.Ghost).Should().BeTrue("the ghost's shape is the hull of sharp boxes");
        RemovesInternalEdges(backend, world.GetOwnedFrame(compound), box, BodyKind.Ghost).Should().BeFalse("the ghost's shape is the box hull");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ship whose hull is a box and whose interior is a compound of boxes of convex radius 0, and a ship whose hull is
    /// such a compound and whose interior is a box. Each body follows its own shape.
    /// </summary>
    [Fact]
    public void TheInteriorOfAFrameRemovesInternalEdgesByItsOwnShapeAndNotByTheHulls()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
        Entity boxHull = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, InteriorShape: TwoSharpBoxes),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        Entity compoundHull = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(100),
            Body = new BodyDesc(TwoSharpBoxes, Mass: 1000, InteriorShape: new BoxShapeDesc(new Vector3(2))),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        world.Tick();

        RemovesInternalEdges(backend, space, boxHull, BodyKind.Primary).Should().BeFalse("the hull is a box");
        RemovesInternalEdges(backend, world.GetOwnedFrame(boxHull), boxHull, BodyKind.Interior).Should().BeTrue("the interior is a compound");
        RemovesInternalEdges(backend, space, compoundHull, BodyKind.Primary).Should().BeTrue("the hull is a compound");
        RemovesInternalEdges(backend, world.GetOwnedFrame(compoundHull), compoundHull, BodyKind.Interior).Should().BeFalse("the interior is a box");
        world.Errors.Should().BeEmpty();
    }
}
