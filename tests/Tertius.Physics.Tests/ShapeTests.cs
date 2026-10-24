using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

public class ShapeTests
{
    [Fact]
    public void EveryShapeKindBuildsInJolt()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        // A mesh floor: two triangles, wound so that they face up.
        var meshFloor = new MeshShapeDesc([new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20), new(-20, 0, 20)],
            [0, 2, 1, 0, 3, 2]);
        world.Spawn(new() { Frame = ground, Pose = DPose.Identity, Body = new(Shape: meshFloor, Motion: BodyMotion.Static) });

        var shapes = new ShapeDesc[]
        {
            new BoxShapeDesc(new(0.5f)),
            new SphereShapeDesc(0.5f),
            new CapsuleShapeDesc(0.5f, 0.25f),
            new ConvexHullShapeDesc([new(-0.5f, 0, -0.5f), new(0.5f, 0, -0.5f), new(0, 0, 0.5f), new(0, 0.8f, 0)]),
            new CompoundShapeDesc(
            [
                new(new BoxShapeDesc(new(0.5f, 0.1f, 0.5f)), Pose.Identity),
                new(new SphereShapeDesc(0.3f), new(new(0, 0.4f, 0), Quaternion.Identity))
            ])
        };
        var bodies = new List<Entity>();
        for (int i = 0; i < shapes.Length; i++)
        {
            // Lay the capsule on its side, so that it does not topple and roll away.
            DQuaternion rotation = shapes[i] is CapsuleShapeDesc
                ? DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2)
                : DQuaternion.Identity;
            bodies.Add(world.Spawn(new()
            {
                Frame = ground,
                Pose = new(new(i * 3 - 6, 2, 0), rotation),
                Body = new(Shape: shapes[i], Mass: 2)
            }));
        }

        Tick(world, 150);

        // Everything fell onto the mesh and came to rest above it.
        foreach (Entity body in bodies)
        {
            world.PoseInOwnFrame(body).Position.Y.Should().BeInRange(-0.02, 1);
            world.VelocityInOwnFrame(body).Length().Should().BeLessThan(0.1f);
        }

        world.Errors.Should().BeEmpty();
    }
}
