// PROBE (t58-rcd6, never commit to src): the diagnosis log of the white room after a cut from outside. Tool code.
using System.Globalization;
using System.Text;

namespace Tertius.Rendering;

/// <summary>
/// Scratch switches of t58-rcd6, read once from the environment:
/// <list type="bullet">
/// <item>TERTIUS_PROBE_CUT_LOG=path: after each drawn frame, one line (VulkanRenderer.ProbeCutLog).</item>
/// <item>TERTIUS_PROBE_CUT_REFRESH=1 (the variant): a draw that resets every history refreshes all six faces of every
/// served map, in that draw's face pass, before any pass reads the maps.</item>
/// </list>
/// </summary>
internal static class CutProbe
{
    public static readonly bool RefreshAtReset = Environment.GetEnvironmentVariable("TERTIUS_PROBE_CUT_REFRESH") == "1";

    private static readonly StreamWriter? Log = Environment.GetEnvironmentVariable("TERTIUS_PROBE_CUT_LOG") is
    { Length: > 0 } path
        ? new StreamWriter(path) { AutoFlush = true }
        : null;

    public static bool Logs => Log is not null;

    private static string faces = "";

    /// <summary>
    /// Notes the faces that this draw refreshes, per served map: "map:frame id:faces", faces as the six letters
    /// XxYyZz (+X, -X, +Y, -Y, +Z, -Z) of the refreshed faces, or '-' for none.
    /// </summary>
    public static void NoteFaces(ReadOnlySpan<FaceMask> masks, ReadOnlySpan<long> served)
    {
        if (Log is null)
            return;
        var text = new StringBuilder();
        for (int map = 0; map < masks.Length; map++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{(map > 0 ? "," : "")}{map}:{served[map]}:");
            int written = 0;
            for (int face = 0; face < CubeFaces.Count; face++)
                if ((masks[map] & CubeFaces.Of((CubeFace)face)) != 0)
                {
                    text.Append("XxYyZz"[face]);
                    written++;
                }
            if (written == 0)
                text.Append('-');
        }
        faces = text.ToString();
    }

    public static string Faces => faces;

    public static void Write(string line) => Log?.WriteLine(line);

    /// <summary>The luminance of a linear color, Rec. 709 weights.</summary>
    public static double Luminance(double r, double g, double b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
