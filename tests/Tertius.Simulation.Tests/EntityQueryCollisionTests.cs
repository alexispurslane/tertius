using System.Numerics;
using AwesomeAssertions;
using Tertius.Physics;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The collision groups that the snapshot publishes for each body, and the collision filters of entity queries, which
/// read them. Every test runs the real world on the real Jolt backend.
/// </summary>
public class EntityQueryCollisionTests
{
    private readonly CollisionGroupRegistry registry = new();

    private static readonly CollisionMask InAnySolid = CollisionMask.Empty.InAny(CollisionGroup.Solid);

    private static World NewWorld(out FrameHandle space)
    {
        World world = WorldTesting.NewWorld(out JoltPhysicsBackend _);
        space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        return world;
    }

    /// <summary>A static box of 1 m in <paramref name="groups"/>, far from the others, with a <see cref="Metal"/> flag.</summary>
    private static Entity Box(World world, FrameHandle frame, double x, CollisionGroups groups, params IComponent[] more) => world.Spawn(new SpawnDesc
    {
        Frame = frame,
        Pose = WorldTesting.At(x),
        Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) with { CollisionGroups = groups },
        Components = [new Metal(), .. more],
    });

    private static List<Entity> Found(FoundEntities run)
    {
        var found = new List<Entity>();
        run.CopyTo(found);
        return found;
    }

    [Fact]
    public void TheSnapshotPublishesTheCollisionGroupsAndMaskThatABodySpawnedWith()
    {
        CollisionGroup wall = registry.Register("Wall");
        using World world = NewWorld(out FrameHandle space);
        Entity box = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(new SphereShapeDesc(0.5f)) with { CollisionGroups = CollisionGroups.Of(wall), CollisionMask = InAnySolid.InAny(wall) },
        });

        world.Snapshot.CollisionOf(box).Should().Be(new BodyCollision(CollisionGroups.Of(wall), InAnySolid.InAny(wall)));
    }

    [Fact]
    public void AnEntityWithoutABodyHasNoCollisionRow()
    {
        using World world = NewWorld(out FrameHandle space);
        Entity plain = world.SpawnWith(space, new Metal());

        world.Snapshot.CollisionOf(plain).Should().BeNull();
    }

    [Fact]
    public void ASetCollisionShowsInTheSnapshotOnlyAfterTheStepThatAppliedIt()
    {
        CollisionGroup glass = registry.Register("Glass");
        using World world = NewWorld(out FrameHandle space);
        var pilot = new Pilot();
        Entity box = Box(world, space, 0, CollisionGroups.Of(CollisionGroup.Solid), pilot);
        world.Tick();
        var before = new BodyCollision(CollisionGroups.Of(CollisionGroup.Solid), InAnySolid);
        var after = new BodyCollision(CollisionGroups.Of(glass), CollisionMask.Empty.InAny(glass));

        pilot.Once = body => body.SetCollision(after.Groups, after.Mask);
        world.Tick();
        BodyCollision? whileInFlight = world.Snapshot.CollisionOf(box);
        world.Tick();

        whileInFlight.Should().Be(before, "the step of the tick that wrote the intent ran before the intent existed");
        world.Snapshot.CollisionOf(box).Should().Be(after, "the step of the next tick applied it");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnIntentWithoutSetCollisionLeavesTheCollisionRowAsItWas()
    {
        CollisionGroup wall = registry.Register("Wall");
        using World world = NewWorld(out FrameHandle space);
        var pilot = new Pilot { EveryTick = body => body.AddForce(new Vector3(0, 1, 0)) };
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(new SphereShapeDesc(0.5f)) with { CollisionGroups = CollisionGroups.Of(wall) },
            Components = [pilot],
        });

        world.Run(3);

        world.Snapshot.CollisionOf(ball).Should().Be(new BodyCollision(CollisionGroups.Of(wall), InAnySolid));
    }

    [Fact]
    public void AQueryKeepsTheEntitiesWhoseBodyIsInEveryInAllGroup()
    {
        CollisionGroup wall = registry.Register("Wall");
        using World world = NewWorld(out FrameHandle space);
        Box(world, space, 0, CollisionGroups.Of(CollisionGroup.Solid));
        Entity solidWall = Box(world, space, 10, CollisionGroups.Of(CollisionGroup.Solid).And(wall));
        Box(world, space, 20, CollisionGroups.Of(wall));

        Found(world.Snapshot.Find(EntityQuery.With<Metal>().InAll(CollisionGroup.Solid).InAll(wall))).Should().Equal(solidWall);
    }

    [Fact]
    public void AQueryKeepsTheEntitiesWhoseBodyIsInAnyGroupOfItsAnyOfSet()
    {
        CollisionGroup wall = registry.Register("Wall"), person = registry.Register("Person");
        using World world = NewWorld(out FrameHandle space);
        Entity aWall = Box(world, space, 0, CollisionGroups.Of(wall));
        Box(world, space, 10, CollisionGroups.Of(CollisionGroup.Solid));
        Entity aPerson = Box(world, space, 20, CollisionGroups.Of(person));

        Found(world.Snapshot.Find(EntityQuery.With<Metal>().InAny(wall).InAny(person))).Should().Equal(aWall, aPerson);
    }

    [Fact]
    public void AQueryDropsTheEntitiesWhoseBodyIsInAnInNoneGroupAndAnEntityWithoutABodyIsInNoGroup()
    {
        CollisionGroup wall = registry.Register("Wall");
        using World world = NewWorld(out FrameHandle space);
        Entity solid = Box(world, space, 0, CollisionGroups.Of(CollisionGroup.Solid));
        Entity solidWall = Box(world, space, 10, CollisionGroups.Of(CollisionGroup.Solid).And(wall));
        Entity plain = world.SpawnWith(space, new Metal());

        Found(world.Snapshot.Find(EntityQuery.With<Metal>().InNone(wall))).Should().Equal(solid, plain);
        Found(world.Snapshot.Find(EntityQuery.With<Metal>().InAny(CollisionGroup.Solid))).Should().Equal(solid, solidWall);
    }

    [Fact]
    public void AQueryReadsTheGroupsThatABodyHasNowNotTheGroupsThatItSpawnedWith()
    {
        CollisionGroup glass = registry.Register("Glass");
        using World world = NewWorld(out FrameHandle space);
        var pilot = new Pilot();
        Entity box = Box(world, space, 0, CollisionGroups.Of(CollisionGroup.Solid), pilot);
        world.Tick();

        pilot.Once = body => body.SetCollision(CollisionGroups.Of(glass), CollisionMask.Empty.InAny(glass));
        world.Run(2);

        Found(world.Snapshot.Find(EntityQuery.InAny(glass))).Should().Equal(box);
        world.Snapshot.Find(EntityQuery.With<Metal>().InAny(CollisionGroup.Solid)).Any().Should().BeFalse();
        world.GetBody(box)!.CollisionGroups.Should().Be(CollisionGroups.Of(CollisionGroup.Solid), "the spawn description stays as it was");
    }
}
