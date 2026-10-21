using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;
using Tertius.Physics;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The physics facts of another entity's body, which any component reads from the snapshot with
/// <see cref="Snapshot.TryGetBody"/>: the body's motion, mass, shape kind and bounding radius, its collision groups and
/// mask, and whether it is a sensor. Every test runs the real world on the real Jolt backend.
/// </summary>
public class BodyFactsTests
{
    private readonly CollisionGroupRegistry registry = new();

    private static readonly CollisionMask InAnySolid = CollisionMask.Empty.InAny(CollisionGroup.Solid);

    /// <summary>The G·M of the point gravity of <see cref="NewWorld"/>'s space, in m³/s², which a Kepler ship orbits.</summary>
    private const double SpaceMu = 5e5;

    private static World NewWorld(out FrameHandle space)
    {
        World world = WorldTesting.NewWorld(out JoltPhysicsBackend _);
        space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), GravityField.Point(SpaceMu)));
        return world;
    }

    private static Entity Spawn(World world, FrameHandle frame, BodyDesc body, double x = 0, params IComponent[] components) => world.Spawn(new SpawnDesc
    {
        Frame = frame,
        Pose = WorldTesting.At(x),
        Body = body,
        Components = components,
    });

    /// <summary>
    /// A ship 5 km from the origin of <paramref name="space"/>: a box hull of 4 m and 1000 kg with <paramref name="motion"/>,
    /// and a bubble of 10 m.
    /// </summary>
    private static Entity Ship(World world, FrameHandle space, BodyMotion motion, ShapeDesc? interior = null) => world.Spawn(new SpawnDesc
    {
        Frame = space,
        Pose = WorldTesting.At(5000),
        Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), motion, Mass: 1000, InteriorShape: interior),
        AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
    });

    /// <summary>A triangle mesh, which only a static body or the interior of a frame can have.</summary>
    private static readonly MeshShapeDesc Ramp = new([new(-5, 0, -5), new(5, 0, -5), new(5, 2, 5), new(-5, 2, 5)], [0, 2, 1, 0, 3, 2]);

    private static BodyFacts FactsOf(World world, Entity entity)
    {
        world.Snapshot.TryGetBody(entity, out BodyFacts facts).Should().BeTrue($"{entity} has a body");
        return facts;
    }

    // ---- Motion ----

    [Theory]
    [InlineData(BodyMotion.Static)]
    [InlineData(BodyMotion.Kinematic)]
    [InlineData(BodyMotion.Dynamic)]
    public void ABodyThatOwnsNoFrameHasTheMotionOfItsDescription(BodyMotion motion)
    {
        using World world = NewWorld(out FrameHandle space);
        Entity box = Spawn(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), motion));

        FactsOf(world, box).Motion.Should().Be(motion);
    }

    [Fact]
    public void ACharacterHasTheCharacterMotion()
    {
        using World world = NewWorld(out FrameHandle space);
        Entity walker = Spawn(world, space, new BodyDesc(new CapsuleShapeDesc(0.5f, 0.3f), BodyMotion.Character, Mass: 70));

        FactsOf(world, walker).Motion.Should().Be(BodyMotion.Character);
    }

    [Theory]
    [InlineData(BodyMotion.Static)]
    [InlineData(BodyMotion.Kinematic)]
    [InlineData(BodyMotion.Dynamic)]
    public void AHullTakesTheMotionOfItsBodyDescription(BodyMotion motion)
    {
        using World world = NewWorld(out FrameHandle space);
        Entity ship = Ship(world, space, motion);
        world.Tick();

        world.Frames.HullMotion(world.GetOwnedFrame(ship)).Should().Be(motion);
        FactsOf(world, ship).Motion.Should().Be(motion);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A root frame has no parent, so its owner has no hull: its body is only the frame's static interior. So a body that
    /// could not be a dynamic hull, without mass or with a mesh, is fine with the default dynamic motion.
    /// </summary>
    [Theory]
    [InlineData("a box without mass")]
    [InlineData("a mesh")]
    public void TheOwnerOfARootFrameHasNoHullMotionWhateverItsBodySays(string body)
    {
        using World world = NewWorld(out _);
        Entity owner = world.Spawn(new SpawnDesc
        {
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f)),
            Body = body == "a mesh" ? new BodyDesc(Ramp) : new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), Mass: 0),
        });
        world.Tick();

        world.Frames.HullMotion(world.GetOwnedFrame(owner)).Should().BeNull();
        FactsOf(world, owner).Motion.Should().Be(BodyMotion.Static, "a root frame has no hull, so the body is only its static interior");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AFrameWithoutABodyHasNoHullMotion()
    {
        using World world = NewWorld(out FrameHandle space);
        Entity room = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = WorldTesting.At(5000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        world.Tick();

        world.Frames.HullMotion(world.GetOwnedFrame(room)).Should().BeNull();
        world.Errors.Should().BeEmpty();
    }

    // ---- Mass and shape ----

    [Fact]
    public void ABodyHasTheMassOfItsDescription()
    {
        using World world = NewWorld(out FrameHandle space);
        Entity crate = Spawn(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 12.5f));

        FactsOf(world, crate).Mass.Should().Be(12.5f);
    }

    [Theory]
    [InlineData(ShapeKind.Box)]
    [InlineData(ShapeKind.Sphere)]
    [InlineData(ShapeKind.Capsule)]
    [InlineData(ShapeKind.ConvexHull)]
    [InlineData(ShapeKind.Mesh)]
    [InlineData(ShapeKind.Compound)]
    public void ABodyNamesTheKindOfItsShape(ShapeKind kind)
    {
        using World world = NewWorld(out FrameHandle space);
        ShapeDesc shape = kind switch
        {
            ShapeKind.Box => new BoxShapeDesc(new Vector3(0.5f)),
            ShapeKind.Sphere => new SphereShapeDesc(0.5f),
            ShapeKind.Capsule => new CapsuleShapeDesc(0.5f, 0.25f),
            ShapeKind.ConvexHull => new ConvexHullShapeDesc([new(-0.5f, 0, -0.5f), new(0.5f, 0, -0.5f), new(0, 0, 0.5f), new(0, 0.8f, 0)]),
            ShapeKind.Mesh => new MeshShapeDesc([new(-1, 0, -1), new(1, 0, -1), new(0, 0, 1)], [0, 2, 1]),
            _ => new CompoundShapeDesc([new CompoundChild(new SphereShapeDesc(0.3f), Pose.Identity)]),
        };
        Entity body = Spawn(world, space, new BodyDesc(shape, BodyMotion.Static));

        FactsOf(world, body).ShapeKind.Should().Be(kind);
    }

    [Fact]
    public void ABodyHasTheRadiusOfTheSmallestSphereAboutItsOriginThatHoldsItsShape()
    {
        using World world = NewWorld(out FrameHandle space);
        Entity box = Spawn(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(1, 2, 2)), Mass: 5));

        FactsOf(world, box).BoundingRadius.Should().BeApproximately(3f, 5e-6f, "the corners of the box are 3 m from its center");
    }

    [Fact]
    public void ABodyHasTheCenterOfMassThatPhysicsComputesForItsShape()
    {
        World world = WorldTesting.NewWorld(out JoltPhysicsBackend backend);
        using (world)
        {
            FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
            // A compound of one box whose center is 1.5 m along +X and 2 m along -Z from the body's origin.
            var offsetBox = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(0.5f)), new Pose(new Vector3(1.5f, 0, -2), Quaternion.Identity))]);
            Entity crate = Spawn(world, space, new BodyDesc(offsetBox, Mass: 5));
            Entity ball = Spawn(world, space, new BodyDesc(new SphereShapeDesc(0.5f)), x: 10);

            Vector3 center = FactsOf(world, crate).CenterOfMass;

            center.Should().Be(backend.ShapeCenterOfMass(offsetBox));
            Vector3.Distance(center, new Vector3(1.5f, 0, -2)).Should().BeLessThan(1e-5f, "the box's center is its center of mass");
            FactsOf(world, ball).CenterOfMass.Should().Be(Vector3.Zero);
        }
    }

    [Fact]
    public void TheCenterOfMassOfAFrameOwnerIsThatOfItsHullAndNotOfItsInterior()
    {
        World world = WorldTesting.NewWorld(out JoltPhysicsBackend backend);
        using (world)
        {
            FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
            var hull = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(2)), new Pose(new Vector3(0, 3, 0), Quaternion.Identity))]);
            Entity ship = world.Spawn(new SpawnDesc
            {
                Frame = space,
                Pose = WorldTesting.At(5000),
                Body = new BodyDesc(hull, BodyMotion.Dynamic, Mass: 1000, InteriorShape: new SphereShapeDesc(8)),
                AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            });

            FactsOf(world, ship).CenterOfMass.Should().Be(backend.ShapeCenterOfMass(hull));
            FactsOf(world, ship).CenterOfMass.Y.Should().BeApproximately(3, 1e-5f);
        }
    }

    [Fact]
    public void ABodyInAWorldWithoutAPhysicsBackendHasItsCenterOfMassAtZero()
    {
        using var test = new TestWorld();
        var offsetBox = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(0.5f)), new Pose(new Vector3(1.5f, 0, -2), Quaternion.Identity))]);
        Entity crate = Spawn(test.World, test.Root, new BodyDesc(offsetBox, Mass: 5));

        FactsOf(test.World, crate).CenterOfMass.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void TheShapeFactsOfAFrameOwnerAreThoseOfItsHullAndNotOfItsInterior()
    {
        using World world = NewWorld(out FrameHandle space);
        Entity ship = Ship(world, space, BodyMotion.Static, interior: new SphereShapeDesc(8));

        BodyFacts facts = FactsOf(world, ship);

        facts.ShapeKind.Should().Be(ShapeKind.Box);
        facts.BoundingRadius.Should().BeApproximately(MathF.Sqrt(12), 5e-6f, "the hull is a box with half extents of 2 m");
    }

    [Fact]
    public void TheFactsOfASensorBodySayThatItIsASensorAndThoseOfASolidBodyDoNot()
    {
        using World world = NewWorld(out FrameHandle space);
        Entity ball = Spawn(world, space, new BodyDesc(new SphereShapeDesc(0.5f)));
        Entity wall = Spawn(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static), x: 10);
        Entity plate = Spawn(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) { IsSensor = true }, x: 20);

        FactsOf(world, ball).IsSensor.Should().BeFalse();
        FactsOf(world, wall).IsSensor.Should().BeFalse();
        FactsOf(world, plate).IsSensor.Should().BeTrue();
    }

    // ---- Collision groups and mask ----

    [Fact]
    public void ABodyHasTheCollisionGroupsAndMaskThatItSpawnedWith()
    {
        CollisionGroup wall = registry.Register("Wall");
        using World world = NewWorld(out FrameHandle space);
        Entity box = Spawn(world, space,
            new BodyDesc(new SphereShapeDesc(0.5f)) with { CollisionGroups = CollisionGroups.Of(wall), CollisionMask = InAnySolid.InAny(wall) });

        BodyFacts facts = FactsOf(world, box);

        facts.CollisionGroups.Should().Be(CollisionGroups.Of(wall));
        facts.CollisionMask.Should().Be(InAnySolid.InAny(wall));
    }

    [Fact]
    public void ASetCollisionShowsInTheFactsOnlyAfterTheStepThatAppliedIt()
    {
        CollisionGroup glass = registry.Register("Glass");
        using World world = NewWorld(out FrameHandle space);
        var pilot = new Pilot();
        Entity box = Spawn(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static), components: [pilot]);
        world.Tick();
        CollisionGroups newGroups = CollisionGroups.Of(glass);
        CollisionMask newMask = CollisionMask.Empty.InAny(glass);

        pilot.Once = body => body.SetCollision(newGroups, newMask);
        world.Tick();
        BodyFacts whileInFlight = FactsOf(world, box);
        world.Tick();
        BodyFacts applied = FactsOf(world, box);

        whileInFlight.CollisionGroups.Should().Be(CollisionGroups.Of(CollisionGroup.Solid), "the step of the tick that wrote the intent ran before the intent existed");
        whileInFlight.CollisionMask.Should().Be(InAnySolid);
        applied.CollisionGroups.Should().Be(newGroups, "the step of the next tick applied it");
        applied.CollisionMask.Should().Be(newMask);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnIntentWithoutSetCollisionLeavesTheCollisionGroupsAsTheyWere()
    {
        CollisionGroup wall = registry.Register("Wall");
        using World world = NewWorld(out FrameHandle space);
        var pilot = new Pilot { EveryTick = body => body.AddForce(new Vector3(0, 1, 0)) };
        Entity ball = Spawn(world, space, new BodyDesc(new SphereShapeDesc(0.5f)) with { CollisionGroups = CollisionGroups.Of(wall) }, components: [pilot]);

        world.Run(3);

        BodyFacts facts = FactsOf(world, ball);
        facts.CollisionGroups.Should().Be(CollisionGroups.Of(wall));
        facts.CollisionMask.Should().Be(InAnySolid);
    }

    // ---- Entities that have no facts ----

    [Fact]
    public void AnEntityWithoutABodyHasNoFacts()
    {
        using World world = NewWorld(out FrameHandle space);
        Entity plain = world.SpawnWith(space, new Metal());

        world.Snapshot.TryGetBody(plain, out BodyFacts facts).Should().BeFalse();
        facts.Should().Be(default(BodyFacts));
    }

    [Fact]
    public void AStaleHandleHasNoFactsEvenWhenItsSlotHoldsANewBody()
    {
        using World world = NewWorld(out FrameHandle space);
        Entity gone = Spawn(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 3));
        world.Destroy(gone);
        Entity heir = Spawn(world, space, new BodyDesc(new SphereShapeDesc(0.5f), Mass: 7));
        heir.Index.Should().Be(gone.Index, "the test needs the new body in the slot of the stale handle");

        world.Snapshot.TryGetBody(gone, out BodyFacts facts).Should().BeFalse();
        facts.Should().Be(default(BodyFacts));
        FactsOf(world, heir).Mass.Should().Be(7);
    }

    // ---- Cost ----

    [Fact]
    public void ReadingTheFactsOfABodyAllocatesNothing()
    {
        using World world = NewWorld(out FrameHandle space);
        Entity[] bodies =
        [
            Spawn(world, space, new BodyDesc(new SphereShapeDesc(0.5f))),
            Spawn(world, space, new BodyDesc(new CapsuleShapeDesc(0.5f, 0.3f), BodyMotion.Character, Mass: 70), x: 10),
            Ship(world, space, BodyMotion.Dynamic),
            world.SpawnWith(space, new Metal()),
        ];
        world.Tick();
        float total = 0;
        for (int warm = 0; warm < 1000; warm++)
            foreach (Entity entity in bodies)
                if (world.Snapshot.TryGetBody(entity, out BodyFacts facts))
                    total += facts.Mass;

        const int rounds = 10_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < rounds; i++)
            foreach (Entity entity in bodies)
                if (world.Snapshot.TryGetBody(entity, out BodyFacts facts))
                    total += facts.BoundingRadius;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Per call, as in GravityAllocationTests: a single compilation of a hot method during the loop does not fail the test.
        long perCall = allocated / (rounds * bodies.Length);
        perCall.Should().Be(0, "a component may read the facts of every entity near it on every tick");
        total.Should().BeGreaterThan(0, "the loop did read facts");
    }

    [Fact]
    public void TheNullEntityHasNoFacts()
    {
        using World world = NewWorld(out _);

        world.Snapshot.TryGetBody(Entity.Null, out BodyFacts facts).Should().BeFalse();
        facts.Should().Be(default(BodyFacts));
    }
}
