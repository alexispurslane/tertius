using System.Numerics;
using Tertius.Simulation;
using static Tertius.Physics.Tests.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Spawn refuses every body that physics cannot create, with an <see cref="ArgumentException"/>, before anything changes.
/// So a change batch that reaches the backend holds only bodies that it can create, and a creation that fails there
/// comes only from a bug. Spawn refuses nothing that physics can create: the owner of a root frame has no hull, so its
/// body only needs to be a valid static interior, whatever the frame's driver.
/// </summary>
public class WorldSpawnRefusalTests
{
    private static readonly Vector3[] Tetrahedron = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];
    private static readonly MeshShapeDesc Triangle = new([new(-50, 0, -50), new(50, 0, -50), new(0, 0, 50)], [0, 2, 1]);

    /// <summary>The bodies that physics cannot create, by the names that the theory uses, and a word that the refusal names.</summary>
    private static (BodyDesc Body, string Word) Refused(string why) => why switch
    {
        "no shape" => (new BodyDesc(null!), "no shape"),
        "a motion that BodyMotion does not define" => (new BodyDesc(new SphereShapeDesc(1), (BodyMotion)9), "BodyMotion"),
        "a layer that BodyLayer does not define" => (new BodyDesc(new SphereShapeDesc(1), Layer: (BodyLayer)9), "BodyLayer"),
        "a box with a negative half extent" => (new BodyDesc(new BoxShapeDesc(new Vector3(1, -1, 1))), "half extents"),
        "a box with a half extent that is not a number" => (new BodyDesc(new BoxShapeDesc(new Vector3(1, float.NaN, 1))), "half extents"),
        "a sphere without a radius" => (new BodyDesc(new SphereShapeDesc(0)), "radius"),
        "a capsule with a negative half height" => (new BodyDesc(new CapsuleShapeDesc(-1, 0.5f)), "half height"),
        "a capsule without a radius" => (new BodyDesc(new CapsuleShapeDesc(1, 0)), "radius"),
        "a convex hull with three points" => (new BodyDesc(new ConvexHullShapeDesc([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)])), "4 points"),
        "a convex hull with a point that is not a number" => (new BodyDesc(new ConvexHullShapeDesc([.. Tetrahedron[..3], new(0, 0, float.NaN)])), "not finite"),
        "a convex hull whose points all coincide" => (new BodyDesc(new ConvexHullShapeDesc([new(1, 1, 1), new(1, 1, 1), new(1, 1, 1), new(1, 1, 1)])), "convex hull"),
        "a mesh without triangles" => (new BodyDesc(new MeshShapeDesc([new(0, 0, 0)], []), BodyMotion.Static), "multiple of 3"),
        "a mesh whose index count is not a multiple of 3" => (new BodyDesc(new MeshShapeDesc(Triangle.Vertices, [0, 2]), BodyMotion.Static), "multiple of 3"),
        "a mesh with an index outside its vertices" => (new BodyDesc(new MeshShapeDesc(Triangle.Vertices, [0, 2, 3]), BodyMotion.Static), "outside"),
        "a mesh with a vertex that is not a number" => (new BodyDesc(new MeshShapeDesc([new(0, 0, 0), new(1, 0, 0), new(float.NaN, 0, 1)], [0, 2, 1]), BodyMotion.Static), "not finite"),
        "a mesh whose triangles are all degenerate" => (new BodyDesc(new MeshShapeDesc([new(0, 0, 0), new(1, 0, 0), new(2, 0, 0)], [0, 1, 2]), BodyMotion.Static), "mesh"),
        "a compound without children" => (new BodyDesc(new CompoundShapeDesc([])), "child"),
        "a compound with a child that is not valid" => (new BodyDesc(new CompoundShapeDesc([new CompoundChild(new SphereShapeDesc(-1), Pose.Identity), new CompoundChild(new SphereShapeDesc(1), Pose.Identity)])), "radius"),
        "a compound with a child whose rotation is zero" => (new BodyDesc(new CompoundShapeDesc([new CompoundChild(new SphereShapeDesc(1), new Pose(Vector3.Zero, default))])), "rotation"),
        "a dynamic body with a mesh" => (new BodyDesc(Triangle), "mesh"),
        "a kinematic body with a compound that holds a mesh" => (new BodyDesc(new CompoundShapeDesc([new CompoundChild(Triangle, Pose.Identity)]), BodyMotion.Kinematic), "mesh"),
        "a character whose shape is not a capsule" => (new BodyDesc(new SphereShapeDesc(0.5f), BodyMotion.Character, Mass: 70), "CapsuleShapeDesc"),
        "a dynamic body without mass" => (new BodyDesc(new SphereShapeDesc(1), Mass: 0), "mass"),
        "a dynamic body whose mass is not a number" => (new BodyDesc(new SphereShapeDesc(1), Mass: float.NaN), "mass"),
        _ => throw new ArgumentOutOfRangeException(nameof(why), why, null),
    };

    [Theory]
    [InlineData("no shape")]
    [InlineData("a motion that BodyMotion does not define")]
    [InlineData("a layer that BodyLayer does not define")]
    [InlineData("a box with a negative half extent")]
    [InlineData("a box with a half extent that is not a number")]
    [InlineData("a sphere without a radius")]
    [InlineData("a capsule with a negative half height")]
    [InlineData("a capsule without a radius")]
    [InlineData("a convex hull with three points")]
    [InlineData("a convex hull with a point that is not a number")]
    [InlineData("a convex hull whose points all coincide")]
    [InlineData("a mesh without triangles")]
    [InlineData("a mesh whose index count is not a multiple of 3")]
    [InlineData("a mesh with an index outside its vertices")]
    [InlineData("a mesh with a vertex that is not a number")]
    [InlineData("a mesh whose triangles are all degenerate")]
    [InlineData("a compound without children")]
    [InlineData("a compound with a child that is not valid")]
    [InlineData("a compound with a child whose rotation is zero")]
    [InlineData("a dynamic body with a mesh")]
    [InlineData("a kinematic body with a compound that holds a mesh")]
    [InlineData("a character whose shape is not a capsule")]
    [InlineData("a dynamic body without mass")]
    [InlineData("a dynamic body whose mass is not a number")]
    public void SpawnRefusesABodyThatPhysicsCannotCreate(string why)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        int live = world.Stats.LiveEntities;
        (BodyDesc body, string word) = Refused(why);

        Action spawn = () => world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 5), Body = body });

        spawn.Should().Throw<ArgumentException>(why).WithMessage($"*{word}*");
        world.Stats.LiveEntities.Should().Be(live, "nothing changed");
        world.Tick();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>The interior of a frame is a static body in the frame's own system, so it must be a shape that physics can build.</summary>
    [Theory]
    [InlineData("a sphere without a radius")]
    [InlineData("a convex hull whose points all coincide")]
    public void SpawnRefusesAFrameWhoseInteriorPhysicsCannotBuild(string why)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        ShapeDesc interior = Refused(why).Body.Shape;

        Action spawn = () => world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static, InteriorShape: interior),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
        });

        spawn.Should().Throw<ArgumentException>(why).WithMessage("*interior*");
    }

    /// <summary>
    /// The reviewer's probe D14. The owner of a frame cannot have a character body. Spawn refuses it, and a ball spawned
    /// right after it in the same gap between ticks gets its body and falls to the floor.
    /// </summary>
    [Fact]
    public void SpawnRefusesAFrameOwnerWithACharacterBodyAndTheNextSpawnGetsItsBody()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        world.Tick();

        Action spawn = () => world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(10, 2),
            AsFrame = new FrameSpec(FrameBounds.Sphere(5), MotionDriver.Physics),
            Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
        });
        spawn.Should().Throw<ArgumentException>().WithMessage("*character*");
        Entity ball = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 5), Body = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 1) });
        Tick(world, 120);

        backend.TryGetBodyFrame(ball, out _).Should().BeTrue();
        world.GetPose(ball).Position.Y.Should().BeApproximately(0.5, 0.01, "the ball fell to the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>A static frame may have a mesh as its interior.</summary>
    [Fact]
    public void AStaticFrameMayHaveAMeshInterior()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));

        Entity hangar = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(100),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(10)), BodyMotion.Static, InteriorShape: Triangle),
            AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static),
        });
        world.Tick();

        backend.HasInterior(hangar).Should().BeTrue();
    }

    /// <summary>
    /// The reviewer's probe D13. A root frame has no parent, so its owner has no hull: its body is only the static interior
    /// of the frame. So any driver is fine for a root frame whose owner's body is a mesh or has no mass, at spawn and later.
    /// A ball lands on the mesh.
    /// </summary>
    [Theory]
    [InlineData("a mesh")]
    [InlineData("a box without mass")]
    public void ARootFrameWithThePhysicsDriverMayHaveABodyThatCouldNotBeAHull(string body)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        BodyDesc interior = body == "a mesh" ? new BodyDesc(Triangle, BodyMotion.Static) : new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), Mass: 0);
        double top = body == "a mesh" ? 0 : 0.5;

        Entity owner = world.Spawn(new SpawnDesc
        {
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Physics, GravityField.Uniform(new Vector3(0, -9.81f, 0))),
            Body = interior,
        });
        FrameHandle root = world.GetOwnedFrame(owner);
        Entity ball = world.Spawn(new SpawnDesc { Frame = root, Pose = At(0, 3), Body = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 1) });
        Tick(world, 120);
        world.SetFrameDriver(root, MotionDriver.Static);
        world.SetFrameDriver(root, MotionDriver.Physics);
        Tick(world, 10);

        world.GetPose(ball).Position.Y.Should().BeApproximately(top + 0.5, 0.01, "the ball rests on the root frame's interior");
        backend.HasInterior(owner).Should().BeTrue();
        backend.TryGetBodyFrame(owner, out _).Should().BeFalse("the owner of a root frame has no hull");
        world.Errors.Should().BeEmpty();
    }
}
