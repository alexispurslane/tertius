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
        Span<Vector3> normals = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        foreach (Vector3 n in normals)
        {
            Vector3 u = MathF.Abs(n.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            Vector3 tangent = Vector3.Normalize(Vector3.Cross(u, n));
            Vector3 bitangent = Vector3.Cross(n, tangent);
            uint start = (uint)vertices.Count;
            for (int i = 0; i < 4; i++)
            {
                float s = (i == 1 || i == 2) ? 1 : -1;
                float t = i >= 2 ? 1 : -1;
                Vector3 p = (n + tangent * s + bitangent * t) * halfExtents;
                vertices.Add(new Vertex
                {
                    Position = p,
                    Normal = n,
                    Tangent = new Vector4(tangent, 1),
                    TexCoord = new Vector2((s + 1) * 0.5f, 1 - (t + 1) * 0.5f),
                });
            }
            indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
        }
        return new MeshData(vertices.ToArray(), indices.ToArray());
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
                var n = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), -MathF.Sin(phi) * MathF.Sin(theta));
                var tangent = new Vector3(-MathF.Sin(theta), 0, -MathF.Cos(theta));
                vertices.Add(new Vertex { Position = n * radius, Normal = n, Tangent = new Vector4(tangent, 1), TexCoord = new Vector2(u, v) });
            }
        }
        uint stride = (uint)segments + 1;
        for (uint r = 0; r < rings; r++)
        {
            for (uint s = 0; s < segments; s++)
            {
                uint a = r * stride + s, b = a + stride;
                indices.AddRange([a, b, a + 1, a + 1, b, b + 1]);
            }
        }
        return new MeshData(vertices.ToArray(), indices.ToArray());
    }

    /// <summary>A flat square in the XZ plane, facing +Y.</summary>
    public static MeshData Plane(float halfSize, float uvRepeat = 1)
    {
        Vertex V(float x, float z, float u, float w) => new()
        {
            Position = new Vector3(x, 0, z),
            Normal = Vector3.UnitY,
            Tangent = new Vector4(1, 0, 0, 1),
            TexCoord = new Vector2(u, w),
        };
        return new MeshData(
            [V(-halfSize, halfSize, 0, uvRepeat), V(halfSize, halfSize, uvRepeat, uvRepeat), V(halfSize, -halfSize, uvRepeat, 0), V(-halfSize, -halfSize, 0, 0)],
            [0, 1, 2, 0, 2, 3]);
    }
}
