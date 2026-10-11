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

    public Shape Get(ShapeDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        if (!shapes.TryGetValue(desc, out Shape? shape))
        {
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
        if (!(smallest >= 0) || !float.IsFinite(half.X + half.Y + half.Z))
            throw new ArgumentException($"Box half extents must be finite and not negative: {half}.");
        // Jolt requires the convex radius to fit inside the box.
        return new BoxShape(half, MathF.Min(Foundation.DefaultConvexRadius, smallest));
    }

    private static SphereShape CreateSphere(float radius)
    {
        if (!(radius > 0) || !float.IsFinite(radius))
            throw new ArgumentException($"A sphere radius must be positive and finite: {radius}.");
        return new SphereShape(radius);
    }

    private static ConvexShape CreateCapsule(CapsuleShapeDesc capsule)
    {
        if (!(capsule.Radius > 0) || !float.IsFinite(capsule.Radius) || !float.IsFinite(capsule.HalfHeight) || capsule.HalfHeight < 0)
            throw new ArgumentException($"Invalid capsule: half height {capsule.HalfHeight}, radius {capsule.Radius}.");
        // Jolt does not accept a capsule without a cylinder part. That capsule is a sphere.
        return capsule.HalfHeight > 0 ? new CapsuleShape(capsule.HalfHeight, capsule.Radius) : new SphereShape(capsule.Radius);
    }

    private static ConvexHullShape CreateHull(ConvexHullShapeDesc hull)
    {
        if (hull.Points is not { Length: >= 4 })
            throw new ArgumentException("A convex hull needs at least 4 points.");
        using var settings = new ConvexHullShapeSettings(hull.Points.AsSpan(), Foundation.DefaultConvexRadius);
        var shape = new ConvexHullShape(settings);
        if (shape.Handle == 0)
            throw new ArgumentException($"Jolt could not build a convex hull from {hull.Points.Length} points. The points may be coplanar or degenerate.");
        return shape;
    }

    private static MeshShape CreateMesh(MeshShapeDesc mesh)
    {
        if (mesh.Vertices is null || mesh.Indices is null || mesh.Indices.Length == 0 || mesh.Indices.Length % 3 != 0)
            throw new ArgumentException("A mesh needs vertices and a non-empty index list whose length is a multiple of 3.");
        var triangles = new IndexedTriangle[mesh.Indices.Length / 3];
        for (int i = 0; i < triangles.Length; i++)
        {
            int a = mesh.Indices[3 * i], b = mesh.Indices[3 * i + 1], c = mesh.Indices[3 * i + 2];
            if ((uint)a >= (uint)mesh.Vertices.Length || (uint)b >= (uint)mesh.Vertices.Length || (uint)c >= (uint)mesh.Vertices.Length)
                throw new ArgumentException($"Mesh triangle {i} has an index outside the {mesh.Vertices.Length} vertices.");
            triangles[i] = new IndexedTriangle(a, b, c);
        }
        using var settings = new MeshShapeSettings(mesh.Vertices.AsSpan(), triangles.AsSpan());
        var shape = new MeshShape(settings);
        if (shape.Handle == 0)
            throw new ArgumentException("Jolt could not build the triangle mesh. Every triangle may be degenerate.");
        return shape;
    }

    private StaticCompoundShape CreateCompound(CompoundShapeDesc compound)
    {
        if (compound.Children is not { Length: > 0 })
            throw new ArgumentException("A compound shape needs at least one child.");
        using var settings = new StaticCompoundShapeSettings();
        foreach (CompoundChild child in compound.Children)
        {
            Pose pose = child.Pose;
            settings.AddShape(pose.Position, Quaternion.Normalize(pose.Rotation), Get(child.Shape));
        }
        var shape = new StaticCompoundShape(settings);
        if (shape.Handle == 0)
            throw new ArgumentException("Jolt could not build the compound shape.");
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
