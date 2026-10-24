using AwesomeAssertions;
using Tertius.Physics;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The collision filters of entity queries, which read the collision groups that the snapshot publishes for each body
/// (see BodyFactsTests). Every test runs the real world on the real Jolt backend.
/// </summary>
public sealed class EntityQueryCollisionTests
{
    private readonly CollisionGroupRegistry registry = new();

    private static World NewWorld(out FrameHandle space)
    {
        World world = WorldTesting.NewWorld(backend: out JoltPhysicsBackend _);
        space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        return world;
    }

    /// <summary>A static box of 1 m in <paramref name="groups"/>, far from the others, with a <see cref="Metal"/> flag.</summary>
    private static Entity Box(
        World world,
        FrameHandle frame,
        double x,
        CollisionGroups groups,
        params IComponent[] more
    ) =>
        world.Spawn(
            new()
            {
                Frame = frame,
                Pose = WorldTesting.At(x),
                Body = new BodyDesc(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Static) with
                {
                    CollisionGroups = groups,
                },
                Components = [new Metal(), .. more],
            }
        );

    private static List<Entity> Found(FoundEntities run)
    {
        var found = new List<Entity>();
        run.CopyTo(found);
        return found;
    }

    [Fact]
    public void AQueryKeepsTheEntitiesWhoseBodyIsInEveryInAllGroup()
    {
        CollisionGroup wall = registry.Register("Wall");
        using World world = NewWorld(out FrameHandle space);
        Box(world: world, frame: space, x: 0, groups: CollisionGroups.Of(CollisionGroup.Solid));
        Entity solidWall = Box(
            world: world,
            frame: space,
            x: 10,
            groups: CollisionGroups.Of(CollisionGroup.Solid).And(wall)
        );
        Box(world: world, frame: space, x: 20, groups: CollisionGroups.Of(wall));

        Found(world.Snapshot.Find(EntityQuery.With<Metal>().InAll(CollisionGroup.Solid).InAll(wall)))
            .Should()
            .Equal(solidWall);
    }

    [Fact]
    public void AQueryKeepsTheEntitiesWhoseBodyIsInAnyGroupOfItsAnyOfSet()
    {
        CollisionGroup wall = registry.Register("Wall"),
            person = registry.Register("Person");
        using World world = NewWorld(out FrameHandle space);
        Entity aWall = Box(world: world, frame: space, x: 0, groups: CollisionGroups.Of(wall));
        Box(world: world, frame: space, x: 10, groups: CollisionGroups.Of(CollisionGroup.Solid));
        Entity aPerson = Box(world: world, frame: space, x: 20, groups: CollisionGroups.Of(person));

        Found(world.Snapshot.Find(EntityQuery.With<Metal>().InAny(wall).InAny(person))).Should().Equal(aWall, aPerson);
    }

    [Fact]
    public void AQueryDropsTheEntitiesWhoseBodyIsInAnInNoneGroupAndAnEntityWithoutABodyIsInNoGroup()
    {
        CollisionGroup wall = registry.Register("Wall");
        using World world = NewWorld(out FrameHandle space);
        Entity solid = Box(world: world, frame: space, x: 0, groups: CollisionGroups.Of(CollisionGroup.Solid));
        Entity solidWall = Box(
            world: world,
            frame: space,
            x: 10,
            groups: CollisionGroups.Of(CollisionGroup.Solid).And(wall)
        );
        Entity plain = world.SpawnWith(space, new Metal());

        Found(world.Snapshot.Find(EntityQuery.With<Metal>().InNone(wall))).Should().Equal(solid, plain);
        Found(world.Snapshot.Find(EntityQuery.With<Metal>().InAny(CollisionGroup.Solid)))
            .Should()
            .Equal(solid, solidWall);
    }

    [Fact]
    public void AQueryReadsTheGroupsThatABodyHasNowNotTheGroupsThatItSpawnedWith()
    {
        CollisionGroup glass = registry.Register("Glass");
        using World world = NewWorld(out FrameHandle space);
        var pilot = new Pilot();
        Entity box = Box(world: world, frame: space, x: 0, groups: CollisionGroups.Of(CollisionGroup.Solid), pilot);
        world.Tick();

        pilot.Once = body => body.SetCollision(CollisionGroups.Of(glass), CollisionMask.Empty.InAny(glass));
        world.Run(2);

        Found(world.Snapshot.Find(EntityQuery.InAny(glass))).Should().Equal(box);
        world.Snapshot.Find(EntityQuery.With<Metal>().InAny(CollisionGroup.Solid)).Any().Should().BeFalse();
        world
            .GetBody(box)!
            .CollisionGroups.Should()
            .Be(CollisionGroups.Of(CollisionGroup.Solid), "the spawn description stays as it was");
    }
}
