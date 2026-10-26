using System.Collections.Immutable;
using System.Numerics;

namespace Tertius;

/// <summary>Procedural meshes for tests, prototypes and the demo.</summary>
public static class Primitives
{
    /// <summary>An axis-aligned box centered on the origin.</summary>
    public static MeshData Box(Vector3 halfExtents)
    {
        var vertices = new List<Vertex>(24);
        var indices = new List<uint>(36);
        Span<Vector3> normals =
        [
            Vector3.UnitX,
            -Vector3.UnitX,
            Vector3.UnitY,
            -Vector3.UnitY,
            Vector3.UnitZ,
            -Vector3.UnitZ,
        ];
        foreach (Vector3 n in normals)
        {
            Vector3 u = MathF.Abs(n.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            var tangent = Vector3.Normalize(Vector3.Cross(u, n));
            var bitangent = Vector3.Cross(n, tangent);
            uint start = (uint)vertices.Count;
            for (int i = 0; i < 4; i++)
            {
                float s = i is 1 or 2 ? 1 : -1;
                float t = i >= 2 ? 1 : -1;
                Vector3 p = (n + tangent * s + bitangent * t) * halfExtents;
                vertices.Add(
                    new()
                    {
                        Position = p,
                        Normal = n,
                        Tangent = new(tangent, 1),
                        TexCoord = new((s + 1) * 0.5f, 1 - (t + 1) * 0.5f),
                    }
                );
            }

            indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
        }

        return new([.. vertices], [.. indices]);
    }

    /// <summary>A UV sphere with outward normals and counterclockwise front faces.</summary>
    public static MeshData Sphere(float radius, int segments = 48, int rings = 24)
    {
        var vertices = new List<Vertex>((segments + 1) * (rings + 1));
        var indices = new List<uint>(segments * rings * 6);
        for (int r = 0; r <= rings; r++)
        {
            float v = (float)r / rings;
            float phi = v * MathF.PI;
            for (int s = 0; s <= segments; s++)
            {
                float u = (float)s / segments;
                float theta = u * MathF.Tau;
                var n = new Vector3(
                    MathF.Sin(phi) * MathF.Cos(theta),
                    MathF.Cos(phi),
                    -MathF.Sin(phi) * MathF.Sin(theta)
                );
                var tangent = new Vector3(-MathF.Sin(theta), 0, -MathF.Cos(theta));
                vertices.Add(
                    new()
                    {
                        Position = n * radius,
                        Normal = n,
                        Tangent = new(tangent, 1),
                        TexCoord = new(u, v),
                    }
                );
            }
        }

        uint stride = (uint)segments + 1;
        for (uint r = 0; r < rings; r++)
        for (uint s = 0; s < segments; s++)
        {
            uint a = r * stride + s,
                b = a + stride;
            indices.AddRange([a, b, a + 1, a + 1, b, b + 1]);
        }

        return new([.. vertices], [.. indices]);
    }

    /// <summary>A flat square in the XZ plane, facing +Y.</summary>
    public static MeshData Plane(float halfSize, float uvRepeat = 1)
    {
        static Vertex V(float x, float z, float u, float w) =>
            new()
            {
                Position = new(x, 0, z),
                Normal = Vector3.UnitY,
                Tangent = new(x: 1, y: 0, z: 0, w: 1),
                TexCoord = new(u, w),
            };

        return new(
            [
                V(x: -halfSize, z: halfSize, u: 0, w: uvRepeat),
                V(x: halfSize, z: halfSize, u: uvRepeat, w: uvRepeat),
                V(x: halfSize, z: -halfSize, u: uvRepeat, w: 0),
                V(x: -halfSize, z: -halfSize, u: 0, w: 0),
            ],
            [0, 1, 2, 0, 2, 3]
        );
    }

    /// <summary>
    /// An open tube along +Y from 0 to <paramref name="length"/> that bends at its middle. Its rig has the joint Base at
    /// the origin and its child Tip at half the length, and its skin blends the two over the middle quarter
    /// (docs/plans/skinning-and-morphs.md, "Part 1: the seam").
    /// </summary>
    public static MeshData BendingCylinder(float radius, float length, int segments = 24, int rings = 16)
    {
        MeshData tube = Tube(radius: radius, length: length, segments: segments, rings: rings);
        var rig = new Rig(BendJoints(length), []);
        return tube with { Rigging = new(rig, BendSkin(tube, rig, length), []) };
    }

    /// <summary>
    /// <see cref="Box"/> with a rig of no joints, no skin and two morphs: Stretch lifts the top to twice the height, and
    /// Lean shears the top along +X by the half width.
    /// </summary>
    public static MeshData MorphingBox(Vector3 halfExtents)
    {
        MeshData box = Box(halfExtents);
        var rig = new Rig([], [new(Name: "Stretch", Rest: 0), new(Name: "Lean", Rest: 0)]);
        var stretch = new MorphTarget(
            Morph: 0,
            Positions: [.. box.Vertices.Select(vertex => TopOnly(vertex, new(0, 2 * halfExtents.Y, 0)))],
            Normals: [],
            Tangents: []
        );
        return box with { Rigging = new(rig, Skin: null, [stretch, Lean(box, halfExtents, morph: 1)]) };
    }

    /// <summary>
    /// <see cref="BendingCylinder"/> with the morph Twist, which turns the ring at height y by (π / 2)(y / length) about
    /// +Y, its positions, normals and tangents alike.
    /// </summary>
    public static MeshData TwistingBendingCylinder(float radius, float length, int segments = 24, int rings = 16)
    {
        MeshData tube = Tube(radius: radius, length: length, segments: segments, rings: rings);
        var rig = new Rig(BendJoints(length), [new(Name: "Twist", Rest: 0)]);
        return tube with { Rigging = new(rig, BendSkin(tube, rig, length), [Twist(tube, length, morph: 0)]) };
    }

    /// <summary>An open tube along +Y from 0 to <paramref name="length"/>, with radial normals and counterclockwise front faces.</summary>
    private static MeshData Tube(float radius, float length, int segments, int rings)
    {
        var vertices = new List<Vertex>((segments + 1) * (rings + 1));
        var indices = new List<uint>(segments * rings * 6);
        for (int r = 0; r <= rings; r++)
        {
            float v = (float)r / rings;
            for (int s = 0; s <= segments; s++)
            {
                float u = (float)s / segments;
                float theta = u * MathF.Tau;
                var n = new Vector3(MathF.Cos(theta), 0, -MathF.Sin(theta));
                var tangent = new Vector3(-MathF.Sin(theta), 0, -MathF.Cos(theta));
                vertices.Add(
                    new()
                    {
                        Position = n * radius + new Vector3(0, v * length, 0),
                        Normal = n,
                        Tangent = new(tangent, 1),
                        TexCoord = new(u, 1 - v),
                    }
                );
            }
        }

        // The rings climb along +Y and the segments turn toward -Z, so (a, a + 1, b) winds counterclockwise from outside.
        uint stride = (uint)segments + 1;
        for (uint r = 0; r < rings; r++)
        for (uint s = 0; s < segments; s++)
        {
            uint a = r * stride + s,
                b = a + stride;
            indices.AddRange([a, a + 1, b, a + 1, b + 1, b]);
        }

        return new([.. vertices], [.. indices]);
    }

    private static ImmutableArray<RigJoint> BendJoints(float length) =>
        [
            new(Name: "Base", Parent: null, Rest: Pose.Identity),
            new(Name: "Tip", Parent: 0, Rest: new(new(0, length / 2, 0), Quaternion.Identity)),
        ];

    /// <summary>
    /// The tube's skin on Base (joint 0) and Tip (joint 1): Tip's weight climbs from 0 to 1 over the middle quarter of
    /// the length, and Base has the rest. The tube is bound in the rest pose.
    /// </summary>
    private static MeshSkin BendSkin(MeshData tube, Rig rig, float length)
    {
        float halfBlend = length / 8;
        VertexSkin[] vertices =
        [
            .. tube.Vertices.Select(vertex =>
            {
                float onTip = Math.Clamp((vertex.Position.Y - (length / 2 - halfBlend)) / (2 * halfBlend), 0, 1);
                return new VertexSkin(
                    Joint0: 0,
                    Joint1: 1,
                    Joint2: 0,
                    Joint3: 0,
                    Weights: new(x: 1 - onTip, y: onTip, z: 0, w: 0)
                );
            }),
        ];

        var modelPoses = new Pose[rig.Joints.Length];
        for (int joint = 0; joint < modelPoses.Length; joint++)
        {
            RigJoint each = rig.Joints[joint];
            modelPoses[joint] = each.Parent is { } parent ? Pose.Compose(modelPoses[parent], each.Rest) : each.Rest;
        }

        return new(vertices, [.. modelPoses.Select(model => model.Inverse())]);
    }

    /// <summary><paramref name="delta"/> for a vertex of the top half, and zero for the others.</summary>
    private static Vector3 TopOnly(in Vertex vertex, Vector3 delta) => vertex.Position.Y > 0 ? delta : Vector3.Zero;

    /// <summary>
    /// The box's shear that moves its top by the half width along +X. A normal turns by the shear's inverse transpose,
    /// which tilts the normals of the ±X faces and leaves the others, and the tangents, which lie along X or Z, as they are.
    /// </summary>
    private static MorphTarget Lean(MeshData box, Vector3 halfExtents, int morph)
    {
        // The shear moves a point by slope × (y + h.y) along +X, so the inverse transpose moves a normal by
        // −slope × n.x along +Y.
        float slope = halfExtents.X / (2 * halfExtents.Y);
        return new(
            Morph: morph,
            Positions: [.. box.Vertices.Select(vertex => TopOnly(vertex, new(halfExtents.X, 0, 0)))],
            Normals:
            [
                .. box.Vertices.Select(vertex =>
                    Vector3.Normalize(vertex.Normal - new Vector3(0, slope * vertex.Normal.X, 0)) - vertex.Normal
                ),
            ],
            Tangents: []
        );
    }

    /// <summary>The deltas that turn the ring at height y by (π / 2)(y / <paramref name="length"/>) about +Y.</summary>
    private static MorphTarget Twist(MeshData tube, float length, int morph)
    {
        int count = tube.Vertices.Length;
        var positions = new Vector3[count];
        var normals = new Vector3[count];
        var tangents = new Vector3[count];
        for (int index = 0; index < count; index++)
        {
            Vertex vertex = tube.Vertices[index];
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2 * vertex.Position.Y / length);
            var tangent = new Vector3(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z);
            positions[index] = Vector3.Transform(vertex.Position, turn) - vertex.Position;
            normals[index] = Vector3.Transform(vertex.Normal, turn) - vertex.Normal;
            tangents[index] = Vector3.Transform(tangent, turn) - tangent;
        }

        return new(Morph: morph, Positions: positions, Normals: normals, Tangents: tangents);
    }
}
