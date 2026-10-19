using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Which Jolt bodies remove the internal edges of their contacts (ter-fb3w): every body whose own shape is a mesh or a
/// compound, or holds one, sets Jolt's <c>EnhancedInternalEdgeRemoval</c>, and every other body leaves it off. That holds
/// for each Jolt body of an entity by its own shape: the body itself, its ghosts, and the interior of the frame that it
/// owns. What the flag does is in the host's RevolvedSolidJointTests: it stops a ball that lands near a joint from being
/// pushed sideways, and a box that slides across joints from tripping.
/// </summary>
/// <remarks>
/// These tests read the flag from Jolt, because no contact shows it on a convex body. Jolt removes the internal edges of
/// a pair when either body has the flag, so against a mesh or a compound, which has it, the convex body's own flag changes
/// nothing. A pair of two convex bodies gives one contact, and Jolt keeps the deepest contact of a pair unchanged, so the
/// flag changes nothing there either. It only costs time.
/// </remarks>
public class InternalEdgeRemovalTests
{
    private static readonly MeshShapeDesc Triangle = new([new(-5, 0, -5), new(5, 0, -5), new(0, 0, 5)], [0, 2, 1]);

    private static readonly CompoundShapeDesc TwoBoxes = new(
    [
        new CompoundChild(new BoxShapeDesc(new Vector3(1)), new Pose(new Vector3(-1, 0, 0), Quaternion.Identity)),
        new CompoundChild(new BoxShapeDesc(new Vector3(1)), new Pose(new Vector3(1, 0, 0), Quaternion.Identity)),
    ]);

    public static TheoryData<string, bool> Shapes() => new()
    {
        { "box", false },
        { "sphere", false },
        { "capsule", false },
        { "convex hull", false },
        { "mesh", true },
        { "compound of boxes", true },
        { "compound that holds a mesh", true },
    };

    private static ShapeDesc Shape(string name) => name switch
    {
        "box" => new BoxShapeDesc(new Vector3(1)),
        "sphere" => new SphereShapeDesc(1),
        "capsule" => new CapsuleShapeDesc(1, 0.5f),
        "convex hull" => new ConvexHullShapeDesc([new(1, 0, 0), new(-1, 0, 0), new(0, 2, 0), new(0, 0, -3)]),
        "mesh" => Triangle,
        "compound of boxes" => TwoBoxes,
        "compound that holds a mesh" => new CompoundShapeDesc([new CompoundChild(Triangle, new Pose(Vector3.Zero, Quaternion.Identity))]),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>The flag of the Jolt body of <paramref name="kind"/> that stands for <paramref name="entity"/> in the system of <paramref name="frame"/>.</summary>
    private static bool RemovesInternalEdges(JoltPhysicsBackend backend, FrameHandle frame, Entity entity, BodyKind kind) =>
        backend.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == kind && BodyUserData.Names(facts.UserData, entity)).EnhancedInternalEdgeRemoval;

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ABodyRemovesInternalEdgesExactlyWhenItsShapeIsOrHoldsAMeshOrACompound(string shape, bool removes)
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
    /// hull a compound of two boxes, the other a box.
    /// </summary>
    [Fact]
    public void AGhostRemovesInternalEdgesExactlyWhenItsShapeIsACompound()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
        Entity compound = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(TwoBoxes, Mass: 1000),
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

        RemovesInternalEdges(backend, world.GetOwnedFrame(box), compound, BodyKind.Ghost).Should().BeTrue("the ghost's shape is the compound hull");
        RemovesInternalEdges(backend, world.GetOwnedFrame(compound), box, BodyKind.Ghost).Should().BeFalse("the ghost's shape is the box hull");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ship whose hull is a box and whose interior is a compound, and a ship whose hull is a compound and whose interior is
    /// a box. Each body follows its own shape.
    /// </summary>
    [Fact]
    public void TheInteriorOfAFrameRemovesInternalEdgesByItsOwnShapeAndNotByTheHulls()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
        Entity boxHull = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, InteriorShape: TwoBoxes),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        Entity compoundHull = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(100),
            Body = new BodyDesc(TwoBoxes, Mass: 1000, InteriorShape: new BoxShapeDesc(new Vector3(2))),
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
