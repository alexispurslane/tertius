using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// Native Jolt shapes, cached by <see cref="ShapeDesc"/> reference. Shape descriptions are immutable and shared, so
/// one native shape serves every body that uses the same description. Not thread-safe: use it at the sync point or
/// on the physics thread, never both at once.
/// </summary>
internal sealed class ShapeCache : IDisposable
{
    private readonly Dictionary<ShapeDesc, Shape> shapes = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The native shape of <paramref name="desc"/>, built on first use. Throws <see cref="ArgumentException"/>, with a
    /// message that completes a sentence that names the shape, when <see cref="ShapeDesc.Problem"/> refuses the shape or
    /// Jolt cannot build it.
    /// </summary>
    public Shape Get(ShapeDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        if (!shapes.TryGetValue(desc, out Shape? shape))
        {
            if (desc.Problem() is { } problem)
                throw new ArgumentException(problem);
            shape = Create(desc);
            shapes.Add(desc, shape);
        }
        return shape;
    }

    public void Dispose()
    {
        foreach (Shape shape in shapes.Values)
            shape.Dispose();
        shapes.Clear();
    }

    private Shape Create(ShapeDesc desc) => desc switch
    {
        BoxShapeDesc box => CreateBox(box),
        SphereShapeDesc sphere => CreateSphere(sphere.Radius),
        CapsuleShapeDesc capsule => CreateCapsule(capsule),
        ConvexHullShapeDesc hull => CreateHull(hull),
        MeshShapeDesc mesh => CreateMesh(mesh),
        CompoundShapeDesc compound => CreateCompound(compound),
        _ => throw new NotSupportedException($"Shape type {desc.GetType().Name} is not supported by the Jolt backend."),
    };

    private static BoxShape CreateBox(BoxShapeDesc box)
    {
        Vector3 half = box.HalfExtents;
        float smallest = MathF.Min(half.X, MathF.Min(half.Y, half.Z));
        // Jolt requires the convex radius to fit inside the box.
        return new BoxShape(half, MathF.Min(Foundation.DefaultConvexRadius, smallest));
    }

    private static SphereShape CreateSphere(float radius) => new(radius);

    private static ConvexShape CreateCapsule(CapsuleShapeDesc capsule)
    {
        // Jolt does not accept a capsule without a cylinder part. That capsule is a sphere.
        return capsule.HalfHeight > 0 ? new CapsuleShape(capsule.HalfHeight, capsule.Radius) : new SphereShape(capsule.Radius);
    }

    private static ConvexHullShape CreateHull(ConvexHullShapeDesc hull)
    {
        using var settings = new ConvexHullShapeSettings(hull.Points.AsSpan(), Foundation.DefaultConvexRadius);
        var shape = new ConvexHullShape(settings);
        if (shape.Handle == 0)
            throw new ArgumentException($"is a convex hull that Jolt cannot build from its {hull.Points.Length} points: they may coincide or lie on a line");
        return shape;
    }

    private static MeshShape CreateMesh(MeshShapeDesc mesh)
    {
        var triangles = new IndexedTriangle[mesh.Indices.Length / 3];
        for (int i = 0; i < triangles.Length; i++)
            triangles[i] = new IndexedTriangle(mesh.Indices[3 * i], mesh.Indices[3 * i + 1], mesh.Indices[3 * i + 2]);
        using var settings = new MeshShapeSettings(mesh.Vertices.AsSpan(), triangles.AsSpan());
        var shape = new MeshShape(settings);
        if (shape.Handle == 0)
            throw new ArgumentException("is a mesh that Jolt cannot build: Jolt drops a triangle whose corners lie on a line, and it may have dropped them all");
        return shape;
    }

    private StaticCompoundShape CreateCompound(CompoundShapeDesc compound)
    {
        using var settings = new StaticCompoundShapeSettings();
        foreach (CompoundChild child in compound.Children)
        {
            Pose pose = child.Pose;
            settings.AddShape(pose.Position, Quaternion.Normalize(pose.Rotation), Get(child.Shape));
        }
        var shape = new StaticCompoundShape(settings);
        if (shape.Handle == 0)
            throw new ArgumentException("is a compound shape that Jolt cannot build");
        return shape;
    }
}

/// <summary>Exact bounding spheres of shape descriptions, about the body origin.</summary>
internal static class ShapeBounds
{
    public static float Radius(ShapeDesc shape)
    {
        double radius = Radius(shape, DPose.Identity);
        // Round up, so that the float radius still bounds the shape.
        float result = (float)radius;
        return result < radius ? MathF.BitIncrement(result) : result;
    }

    private static double Radius(ShapeDesc shape, in DPose pose)
    {
        switch (shape)
        {
            case BoxShapeDesc box:
            {
                double max = 0;
                Vector3 h = box.HalfExtents;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = new Double3((corner & 1) != 0 ? h.X : -h.X, (corner & 2) != 0 ? h.Y : -h.Y, (corner & 4) != 0 ? h.Z : -h.Z);
                    max = Math.Max(max, pose.TransformPoint(p).Length);
                }
                return max;
            }
            case SphereShapeDesc sphere:
                return pose.Position.Length + sphere.Radius;
            case CapsuleShapeDesc capsule:
            {
                double top = pose.TransformPoint(new Double3(0, capsule.HalfHeight, 0)).Length;
                double bottom = pose.TransformPoint(new Double3(0, -capsule.HalfHeight, 0)).Length;
                return Math.Max(top, bottom) + capsule.Radius;
            }
            case ConvexHullShapeDesc hull:
            {
                if (hull.Points is null)
                    throw new ArgumentException("A convex hull needs points.");
                double max = 0;
                foreach (Vector3 p in hull.Points)
                    max = Math.Max(max, pose.TransformPoint(p).Length);
                return max;
            }
            case MeshShapeDesc mesh:
            {
                if (mesh.Vertices is null || mesh.Indices is null)
                    throw new ArgumentException("A mesh needs vertices and indices.");
                // Only vertices that a triangle uses are part of the collision shape.
                double max = 0;
                foreach (int index in mesh.Indices)
                {
                    if ((uint)index >= (uint)mesh.Vertices.Length)
                        throw new ArgumentException($"Mesh index {index} is outside the {mesh.Vertices.Length} vertices.");
                    max = Math.Max(max, pose.TransformPoint(mesh.Vertices[index]).Length);
                }
                return max;
            }
            case CompoundShapeDesc compound:
            {
                if (compound.Children is null)
                    throw new ArgumentException("A compound shape needs children.");
                double max = 0;
                foreach (CompoundChild child in compound.Children)
                {
                    var local = new DPose(child.Pose.Position, DQuaternion.FromQuaternion(Quaternion.Normalize(child.Pose.Rotation)));
                    max = Math.Max(max, Radius(child.Shape, DPose.Compose(pose, local)));
                }
                return max;
            }
            default:
                throw new NotSupportedException($"Shape type {shape.GetType().Name} is not supported by the Jolt backend.");
        }
    }
}
