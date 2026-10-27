using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The strand's octahedral code of two bytes in the shading target's R and G, the C# twin of strandCode and strandOfCode
/// in the shaders (docs/plans/skin-and-hair.md, "The shading target").
/// </summary>
internal static class StrandCode
{
    /// <summary>The code of the unit <paramref name="strand"/>.</summary>
    public static (byte X, byte Y) Encode(Vector3 strand)
    {
        Vector2 folded = OctahedralNormal.Encode(strand);
        return (X: ToByte(folded.X), Y: ToByte(folded.Y));
    }

    /// <summary>The unit strand of the code (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static Vector3 Decode(byte x, byte y) => OctahedralNormal.Decode(new Vector2(x, y) / 255 * 2 - Vector2.One);

    // floor(v + 0.5) rounds, as in the shader, whose round leaves its ties to the driver.
    private static byte ToByte(float folded) =>
        (byte)Math.Clamp(MathF.Floor((folded * 0.5f + 0.5f) * 255 + 0.5f), 0, 255);
}
