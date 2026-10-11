using System.Numerics;

namespace Tertius.Physics.Tests;

public class ShapeTests
{
    [Fact]
    public void BoundingRadiusOfEachShape()
    {
        using var backend = new JoltPhysicsBackend();
        backend.BoundingRadius(new BoxShapeDesc(new Vector3(1, 2, 2))).Should().BeApproximately(3f, 5e-6f);
        backend.BoundingRadius(new SphereShapeDesc(1.5f)).Should().Be(1.5f);
        backend.BoundingRadius(new CapsuleShapeDesc(1, 0.5f)).Should().BeApproximately(1.5f, 5e-6f);
        backend.BoundingRadius(new CapsuleShapeDesc(0, 0.5f)).Should().BeApproximately(0.5f, 5e-6f);

        var hull = new ConvexHullShapeDesc([new(1, 0, 0), new(-1, 0, 0), new(0, 2, 0), new(0, 0, -3)]);
        backend.BoundingRadius(hull).Should().BeApproximately(3f, 5e-6f);

        // A vertex that no triangle uses is not part of the shape.
        var mesh = new MeshShapeDesc([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(100, 100, 100)], [0, 1, 2]);
        backend.BoundingRadius(mesh).Should().BeApproximately(1f, 5e-6f);

        // Children with poses: a unit sphere at x = 3, and a unit cube at y = -3 turned 45° about Z. The cube's
        // farthest corner is at (0, -3 - √2, ±1).
        var compound = new CompoundShapeDesc(
        [
            new CompoundChild(new SphereShapeDesc(1), new Pose(new Vector3(3, 0, 0), Quaternion.Identity)),
            new CompoundChild(new BoxShapeDesc(new Vector3(1)), new Pose(new Vector3(0, -3, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4))),
        ]);
        double expected = Math.Sqrt((3 + Math.Sqrt(2)) * (3 + Math.Sqrt(2)) + 1);
        ((double)backend.BoundingRadius(compound)).Should().BeApproximately(expected, 5e-5);

        // Nested compounds compose their poses.
        var nested = new CompoundShapeDesc([new CompoundChild(compound, new Pose(new Vector3(0, 0, 10), Quaternion.Identity))]);
        ((double)backend.BoundingRadius(nested)).Should().BeApproximately(Math.Sqrt((3 + Math.Sqrt(2)) * (3 + Math.Sqrt(2)) + 121), 5e-5);
    }

    [Fact]
    public void BoundingRadiusIsAnUpperBound()
    {
        using var backend = new JoltPhysicsBackend();
        var box = new BoxShapeDesc(new Vector3(0.1f, 0.2f, 0.3f));
        float radius = backend.BoundingRadius(box);
        ((double)radius).Should().BeGreaterThanOrEqualTo(Math.Sqrt(0.1f * 0.1f + 0.2f * 0.2f + 0.3f * 0.3f));
    }

    [Fact]
    public void EveryShapeKindBuildsInJolt()
    {
        using var h = new PhysicsHarness();
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        // A mesh floor: two triangles, wound so that they face up.
        var meshFloor = new MeshShapeDesc([new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20), new(-20, 0, 20)], [0, 2, 1, 0, 3, 2]);
        h.AddBody(ground, new BodyDesc(meshFloor, BodyMotion.Static), DPose.Identity);

        var shapes = new ShapeDesc[]
        {
            new BoxShapeDesc(new Vector3(0.5f)),
            new SphereShapeDesc(0.5f),
            new CapsuleShapeDesc(0.5f, 0.25f),
            new ConvexHullShapeDesc([new(-0.5f, 0, -0.5f), new(0.5f, 0, -0.5f), new(0, 0, 0.5f), new(0, 0.8f, 0)]),
            new CompoundShapeDesc(
            [
                new CompoundChild(new BoxShapeDesc(new Vector3(0.5f, 0.1f, 0.5f)), Pose.Identity),
                new CompoundChild(new SphereShapeDesc(0.3f), new Pose(new Vector3(0, 0.4f, 0), Quaternion.Identity)),
            ]),
        };
        var bodies = new List<Entity>();
        for (int i = 0; i < shapes.Length; i++)
        {
            // Lay the capsule on its side, so that it does not topple and roll away.
            DQuaternion rotation = shapes[i] is CapsuleShapeDesc ? DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2) : DQuaternion.Identity;
            bodies.Add(h.AddBody(ground, new BodyDesc(shapes[i], Mass: 2), new DPose(new Double3(i * 3 - 6, 2, 0), rotation)));
        }

        h.Step(150);

        // Everything fell onto the mesh and came to rest above it.
        foreach (Entity body in bodies)
        {
            h[body].Pose.Position.Y.Should().BeInRange(-0.02, 1);
            h[body].LinearVelocity.Length().Should().BeLessThan(0.1f);
        }
    }

    [Fact]
    public void LayerTableMatchesTheArchitecture()
    {
        BodyLayer[] layers = [BodyLayer.Static, BodyLayer.Moving, BodyLayer.FrameHull, BodyLayer.Ghost];
        bool[,] expected =
        {
            //            Static  Moving  Hull   Ghost
            /* Static */ { false, true,   true,  false },
            /* Moving */ { true,  true,   false, true },
            /* Hull   */ { true,  false,  true,  false },
            /* Ghost  */ { false, true,   false, false },
        };
        for (int a = 0; a < 4; a++)
            for (int b = 0; b < 4; b++)
                Layers.Collide(layers[a], layers[b]).Should().Be(expected[a, b], "the table says so for {0} against {1}", layers[a], layers[b]);
    }
}
