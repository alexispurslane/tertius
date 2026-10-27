using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// A C# copy of the specular prefilter (docs/plans/environment-map.md, "The specular levels"), for the GPU tests: Karis's
/// sum over the Hammersley samples of a level, each a trilinear read of a radiance map that the test read back. The reads
/// cross a face's edge into the next face, as Vulkan's cube filtering does. At a corner of the cube, where Vulkan takes
/// the mean of three texels, this copy takes the one that the direction through the missing texel's center meets.
/// </summary>
/// <param name="mips">Each level of the radiance map, then each face in layer order, RGBA, rows from the top.</param>
/// <param name="faceSize">The texels along a side of a face of level 0.</param>
internal sealed class PrefilterReference(Half[][][] mips, int faceSize)
{
    private readonly Half[][][] mips = mips;
    private readonly int faceSize = faceSize;

    /// <summary>The prefilter of roughness <paramref name="roughness"/> around the unit <paramref name="n"/>, RGBA.</summary>
    public Vector4 Prefilter(Vector3 n, float roughness)
    {
        const int samples = PrefilterLevels.SamplesPerTexel;
        var lobe = new GgxLobe(roughness);
        var points = new Hammersley(samples);
        // The shader's frame around n.
        Vector3 helper = MathF.Abs(n.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX;
        var tangent = Vector3.Normalize(Vector3.Cross(helper, n));
        var bitangent = Vector3.Cross(n, tangent);
        Vector4 sum = Vector4.Zero;
        double weight = 0;
        for (int i = 0; i < samples; i++)
        {
            (double u, double v) = points[i];
            Double3 h = lobe.SampleHalfVector(u, v);
            // l = 2 (n·h) h − n, with n = (0, 0, 1) in the frame.
            var l = new Vector3((float)(2 * h.Z * h.X), (float)(2 * h.Z * h.Y), (float)(2 * h.Z * h.Z - 1));
            if (l.Z <= 0)
                continue;
            float pdf = (float)(lobe.Distribution(h.Z) / 4);
            float lod = PrefilterLevels.SampleLod(pdf, samples, faceSize);
            Vector3 direction = tangent * l.X + bitangent * l.Y + n * l.Z;
            sum += Trilinear(direction, lod) * l.Z;
            weight += l.Z;
        }

        return sum / (float)weight;
    }

    /// <summary>The trilinear read of the radiance map in <paramref name="direction"/> at level <paramref name="lod"/>.</summary>
    private Vector4 Trilinear(Vector3 direction, float lod)
    {
        int last = mips.Length - 1;
        lod = Math.Clamp(lod, 0, last);
        int lower = (int)MathF.Floor(lod);
        int upper = Math.Min(lower + 1, last);
        float share = lod - lower;
        return Vector4.Lerp(Bilinear(direction, lower), Bilinear(direction, upper), share);
    }

    private Vector4 Bilinear(Vector3 direction, int level)
    {
        int size = faceSize >> level;
        (CubeFace face, float s, float t) = CubeFaces.Locate(direction);
        float x = s * size - 0.5f;
        float y = t * size - 0.5f;
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float a = x - x0;
        float b = y - y0;
        return (1 - a) * (1 - b) * Texel(level: level, face: face, x: x0, y: y0)
            + a * (1 - b) * Texel(level: level, face: face, x: x0 + 1, y: y0)
            + (1 - a) * b * Texel(level: level, face: face, x: x0, y: y0 + 1)
            + a * b * Texel(level: level, face: face, x: x0 + 1, y: y0 + 1);
    }

    /// <summary>Texel (x, y) of a face of a level, or the texel of the next face that a texel past the edge stands for.</summary>
    private Vector4 Texel(int level, CubeFace face, int x, int y)
    {
        int size = faceSize >> level;
        if (x < 0 || y < 0 || x >= size || y >= size)
        {
            FaceBasis basis = CubeFaces.Basis(face);
            float s = (x + 0.5f) / size;
            float t = (y + 0.5f) / size;
            Vector3 beyond = basis.Forward + (2 * s - 1) * basis.Right + (1 - 2 * t) * basis.Up;
            (CubeFace next, float ns, float nt) = CubeFaces.Locate(beyond);
            face = next;
            x = Math.Clamp((int)MathF.Floor(ns * size), 0, size - 1);
            y = Math.Clamp((int)MathF.Floor(nt * size), 0, size - 1);
        }

        return EnvironmentScenes.Texel(face: mips[level][(int)face], size: size, x: x, y: y);
    }
}
