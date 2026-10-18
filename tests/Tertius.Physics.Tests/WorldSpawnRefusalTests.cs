using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Spawn refuses every body that physics cannot create, with an <see cref="ArgumentException"/>, before anything changes.
/// So a change batch that reaches the backend holds only bodies that it can create, and a creation that fails there
/// comes only from a bug. Spawn refuses nothing that physics can create: the owner of a root frame has no hull, so its
/// body only needs to be a valid static interior, whatever the motion of its description.
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
        "a dynamic body whose mass is infinite" => (new BodyDesc(new SphereShapeDesc(1), Mass: float.PositiveInfinity), "mass"),
        "a dynamic body whose mass is too small for a float to hold its inverse" => (new BodyDesc(new SphereShapeDesc(1), Mass: 1e-40f), "mass"),
        "a kinematic body whose mass is not a number" => (new BodyDesc(new SphereShapeDesc(1), BodyMotion.Kinematic, Mass: float.NaN), "mass"),
        "a static body with a negative mass" => (new BodyDesc(new SphereShapeDesc(1), BodyMotion.Static, Mass: -1), "mass"),
        "a character whose mass is too small for a float to hold its inverse" => (new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 1e-40f), "mass"),
        "a body whose friction is not a number" => (new BodyDesc(new SphereShapeDesc(1), Friction: float.NaN), "friction"),
        "a body whose restitution is infinite" => (new BodyDesc(new SphereShapeDesc(1), Restitution: float.PositiveInfinity), "restitution"),
        "a body whose linear damping is not a number" => (new BodyDesc(new SphereShapeDesc(1), LinearDamping: float.NaN), "linear damping"),
        "a body whose angular damping is infinite" => (new BodyDesc(new SphereShapeDesc(1), AngularDamping: float.NegativeInfinity), "angular damping"),
        "a body whose collision mask has an all-of part" => (new BodyDesc(new SphereShapeDesc(1)) { CollisionMask = CollisionMask.Empty.InAny(CollisionGroup.Solid).InAll(CollisionGroup.Solid) }, "all-of"),
        "a body whose collision mask has a none-of part" => (new BodyDesc(new SphereShapeDesc(1)) { CollisionMask = CollisionMask.Empty.InNone(CollisionGroup.Solid) }, "none-of"),
        "a character that is a sensor" => (new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70) { IsSensor = true }, "sensor"),
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
    [InlineData("a dynamic body whose mass is infinite")]
    [InlineData("a dynamic body whose mass is too small for a float to hold its inverse")]
    [InlineData("a kinematic body whose mass is not a number")]
    [InlineData("a static body with a negative mass")]
    [InlineData("a character whose mass is too small for a float to hold its inverse")]
    [InlineData("a body whose friction is not a number")]
    [InlineData("a body whose restitution is infinite")]
    [InlineData("a body whose linear damping is not a number")]
    [InlineData("a body whose angular damping is infinite")]
    [InlineData("a body whose collision mask has an all-of part")]
    [InlineData("a body whose collision mask has a none-of part")]
    [InlineData("a character that is a sensor")]
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

    /// <summary>
    /// The hull of a frame is a body too. Its mass counts while the hull is dynamic, and a hull with another motion keeps it
    /// for a later change to dynamic, so it must be a mass that physics can use, whatever the motion.
    /// </summary>
    [Theory]
    [InlineData(BodyMotion.Dynamic, float.PositiveInfinity)]
    [InlineData(BodyMotion.Dynamic, 1e-40f)]
    [InlineData(BodyMotion.Static, 1e-40f)]
    [InlineData(BodyMotion.Static, float.NaN)]
    public void SpawnRefusesAFrameWhoseHullHasAMassThatPhysicsCannotUse(BodyMotion motion, float mass)
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        int live = world.Stats.LiveEntities;

        Action spawn = () => world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(100),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), motion, Mass: mass),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });

        spawn.Should().Throw<ArgumentException>().WithMessage("*mass*");
        world.Stats.LiveEntities.Should().Be(live, "nothing changed");
    }

    /// <summary>
    /// A frame 100 m along +X in <paramref name="space"/>, whose owner has the hull <paramref name="hull"/>.
    /// </summary>
    private static Action SpawnHull(World world, FrameHandle space, BodyDesc hull) => () => world.Spawn(new SpawnDesc
    {
        Frame = space,
        Pose = At(100),
        Body = hull,
        AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
    });

    /// <summary>A hull with a mesh, which Jolt never moves, by the names that the theory uses.</summary>
    private static ShapeDesc MeshOf(string shape) => shape == "a mesh" ? Triangle : new CompoundShapeDesc([new CompoundChild(Triangle, Pose.Identity)]);

    /// <summary>A hull whose shape is or holds a mesh can only be static, because Jolt never moves a mesh.</summary>
    [Theory]
    [InlineData("a mesh", BodyMotion.Kinematic)]
    [InlineData("a mesh", BodyMotion.Dynamic)]
    [InlineData("a mesh in a compound", BodyMotion.Kinematic)]
    [InlineData("a mesh in a compound", BodyMotion.Dynamic)]
    public void SpawnRefusesAMeshHullWhoseBodyIsNotStatic(string shape, BodyMotion motion)
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        int live = world.Stats.LiveEntities;

        Action spawn = SpawnHull(world, space, new BodyDesc(MeshOf(shape), motion, Mass: 1000));

        spawn.Should().Throw<ArgumentException>().WithMessage($"*{motion.ToString().ToLowerInvariant()}*mesh*");
        world.Stats.LiveEntities.Should().Be(live, "nothing changed");
        world.Stats.Frames.Should().Be(1, "only the space exists");
    }

    /// <summary>
    /// The reviewer's probe S8. A dynamic hull needs a mass, or physics could not move it, and the frame would never get a
    /// state from physics. So every frame with a dynamic hull moves with its hull.
    /// </summary>
    [Fact]
    public void SpawnRefusesADynamicHullWithoutMass()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        int live = world.Stats.LiveEntities;

        Action spawn = SpawnHull(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Dynamic, Mass: 0));

        spawn.Should().Throw<ArgumentException>().WithMessage("*dynamic*mass*");
        world.Stats.LiveEntities.Should().Be(live, "nothing changed");
        world.Stats.Frames.Should().Be(1, "only the space exists");
    }

    /// <summary>Only a dynamic hull moves by its mass, so a static or a kinematic hull needs none.</summary>
    [Theory]
    [InlineData(BodyMotion.Static)]
    [InlineData(BodyMotion.Kinematic)]
    public void SpawnAcceptsAHullWithoutMassThatIsNotDynamic(BodyMotion motion)
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));

        SpawnHull(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(2)), motion, Mass: 0))();
        world.Tick();

        world.Stats.Frames.Should().Be(2);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>A hull whose shape is or holds a mesh can be static.</summary>
    [Theory]
    [InlineData("a mesh")]
    [InlineData("a mesh in a compound")]
    public void SpawnAcceptsAStaticMeshHull(string shape)
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));

        SpawnHull(world, space, new BodyDesc(MeshOf(shape), BodyMotion.Static))();
        world.Tick();

        world.Stats.Frames.Should().Be(2);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A pose or a velocity that is not finite, or a rotation that is not a number or has no length, turns the body into NaN
    /// in the first physics step. Spawn refuses it, for an entity with a body and for one without, and nothing changes.
    /// </summary>
    [Theory]
    [InlineData("a position that is not a number", true)]
    [InlineData("an infinite position", true)]
    [InlineData("the zero rotation of a default pose", true)]
    [InlineData("a rotation that is not a number", true)]
    [InlineData("a linear velocity that is not a number", true)]
    [InlineData("an infinite angular velocity", true)]
    [InlineData("a position that is not a number", false)]
    [InlineData("the zero rotation of a default pose", false)]
    [InlineData("an infinite angular velocity", false)]
    public void SpawnRefusesAPoseOrVelocityThatIsNotFinite(string why, bool withBody)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity ball = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0.3, 3), Body = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 1) });
        int live = world.Stats.LiveEntities;
        DPose pose = At(0, 1);
        Vector3 linear = Vector3.Zero, angular = Vector3.Zero;
        string word = "pose";
        switch (why)
        {
            case "a position that is not a number": pose = pose with { Position = new Double3(double.NaN, 1, 0) }; break;
            case "an infinite position": pose = pose with { Position = new Double3(0, double.PositiveInfinity, 0) }; break;
            case "the zero rotation of a default pose": pose = default; break;
            case "a rotation that is not a number": pose = pose with { Rotation = new DQuaternion(double.NaN, 0, 0, 1) }; break;
            case "a linear velocity that is not a number": linear = new Vector3(float.NaN, 0, 0); word = "linear velocity"; break;
            case "an infinite angular velocity": angular = new Vector3(0, float.PositiveInfinity, 0); word = "angular velocity"; break;
            default: throw new ArgumentOutOfRangeException(nameof(why), why, null);
        }

        Action spawn = () => world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = pose,
            LinearVelocity = linear,
            AngularVelocity = angular,
            Body = withBody ? Cube() : null,
        });

        spawn.Should().Throw<ArgumentException>(why).WithMessage($"*{word}*");
        world.Stats.LiveEntities.Should().Be(live, "nothing changed");
        Tick(world, 120);
        world.PoseInOwnFrame(ball).Position.Y.Should().BeApproximately(0.5, 0.01, "the ball fell onto the floor next to nothing");
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
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
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
            AsFrame = new FrameSpec(FrameBounds.Sphere(5)),
            Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
        });
        spawn.Should().Throw<ArgumentException>().WithMessage("*character*");
        Entity ball = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 5), Body = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 1) });
        Tick(world, 120);

        backend.TryGetBodyFrame(ball, out _).Should().BeTrue();
        world.PoseInOwnFrame(ball).Position.Y.Should().BeApproximately(0.5, 0.01, "the ball fell to the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>The owner of a frame cannot be a sensor: its hull and its interior push, whatever its motion.</summary>
    [Theory]
    [InlineData(BodyMotion.Static)]
    [InlineData(BodyMotion.Dynamic)]
    public void SpawnRefusesAFrameOwnerWhoseBodyIsASensor(BodyMotion motion)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        int live = world.Stats.LiveEntities;

        Action spawn = () => world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 5),
            AsFrame = new FrameSpec(FrameBounds.Sphere(5)),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), motion, Mass: 10) { IsSensor = true },
        });

        spawn.Should().Throw<ArgumentException>().WithMessage("*sensor*");
        world.Stats.LiveEntities.Should().Be(live, "nothing changed");
    }

    /// <summary>A static frame may have a mesh as its interior.</summary>
    [Fact]
    public void AStaticFrameMayHaveAMeshInterior()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));

        Entity hangar = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(100),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(10)), BodyMotion.Static, InteriorShape: Triangle),
            AsFrame = new FrameSpec(FrameBounds.Sphere(50)),
        });
        world.Tick();

        backend.HasInterior(hangar).Should().BeTrue();
    }

    /// <summary>
    /// The reviewer's probe D13. A root frame has no parent, so its owner has no hull: its body is only the static interior
    /// of the frame. So the owner of a root frame may have a body that could not be a hull: a mesh, or a box without mass
    /// with the default dynamic motion. A ball lands on it.
    /// </summary>
    [Theory]
    [InlineData("a mesh")]
    [InlineData("a box without mass")]
    public void ARootFrameMayHaveABodyThatCouldNotBeAHull(string body)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        BodyDesc interior = body == "a mesh" ? new BodyDesc(Triangle, BodyMotion.Static) : new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), Mass: 0);
        double top = body == "a mesh" ? 0 : 0.5;

        Entity owner = world.Spawn(new SpawnDesc
        {
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform(new Vector3(0, -9.81f, 0))),
            Body = interior,
        });
        FrameHandle root = world.GetOwnedFrame(owner);
        Entity ball = world.Spawn(new SpawnDesc { Frame = root, Pose = At(0, 3), Body = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 1) });
        Tick(world, 130);

        world.PoseInOwnFrame(ball).Position.Y.Should().BeApproximately(top + 0.5, 0.01, "the ball rests on the root frame's interior");
        backend.HasInterior(owner).Should().BeTrue();
        backend.TryGetBodyFrame(owner, out _).Should().BeFalse("the owner of a root frame has no hull");
        world.Errors.Should().BeEmpty();
    }
}
