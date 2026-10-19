using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// Native Jolt shapes, cached by <see cref="ShapeDesc"/> reference. Shape descriptions are immutable and shared, so
/// one native shape serves every body that uses the same description. Two descriptions that differ only in their
/// convex radius are two keys and give two native shapes. Not thread-safe: use it at the sync point or
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
        return NativeOwnership.Owned(new BoxShape(half, MathF.Min(box.ConvexRadius, smallest)));
    }

    private static SphereShape CreateSphere(float radius) => NativeOwnership.Owned(new SphereShape(radius));

    private static ConvexShape CreateCapsule(CapsuleShapeDesc capsule)
    {
        // Jolt does not accept a capsule without a cylinder part. That capsule is a sphere.
        return capsule.HalfHeight > 0 ? NativeOwnership.Owned(new CapsuleShape(capsule.HalfHeight, capsule.Radius)) : CreateSphere(capsule.Radius);
    }

    private static ConvexHullShape CreateHull(ConvexHullShapeDesc hull)
    {
        using ConvexHullShapeSettings settings = NativeOwnership.Owned(new ConvexHullShapeSettings(hull.Points.AsSpan(), hull.ConvexRadius));
        ConvexHullShape shape = NativeOwnership.Owned(new ConvexHullShape(settings));
        if (shape.Handle == 0)
            throw new ArgumentException($"is a convex hull that Jolt cannot build from its {hull.Points.Length} points: they may coincide or lie on a line");
        return shape;
    }

    private static MeshShape CreateMesh(MeshShapeDesc mesh)
    {
        var triangles = new IndexedTriangle[mesh.Indices.Length / 3];
        for (int i = 0; i < triangles.Length; i++)
            triangles[i] = new IndexedTriangle(mesh.Indices[3 * i], mesh.Indices[3 * i + 1], mesh.Indices[3 * i + 2]);
        using MeshShapeSettings settings = NativeOwnership.Owned(new MeshShapeSettings(mesh.Vertices.AsSpan(), triangles.AsSpan()));
        MeshShape shape = NativeOwnership.Owned(new MeshShape(settings));
        if (shape.Handle == 0)
            throw new ArgumentException("is a mesh that Jolt cannot build: Jolt drops a triangle whose corners lie on a line, and it may have dropped them all");
        return shape;
    }

    private StaticCompoundShape CreateCompound(CompoundShapeDesc compound)
    {
        using StaticCompoundShapeSettings settings = NativeOwnership.Owned(new StaticCompoundShapeSettings());
        foreach (CompoundChild child in compound.Children)
        {
            Pose pose = child.Pose;
            settings.AddShape(pose.Position, Quaternion.Normalize(pose.Rotation), Get(child.Shape));
        }
        StaticCompoundShape shape = NativeOwnership.Owned(new StaticCompoundShape(settings));
        if (shape.Handle == 0)
            throw new ArgumentException("is a compound shape that Jolt cannot build");
        return shape;
    }
}
