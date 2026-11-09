using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tertius.LtcFit;

/// <summary>
/// Fits the LTC table to the GGX lobe of Tertius.Rendering and writes it to the path in the first argument
/// (tools/README.md, "The LTC table"). A class of its own, not top-level statements, so that its name does not clash with
/// the other tools that the renderer's tests reference.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is not [var path])
        {
            Console.Error.WriteLine("Usage: Tertius.LtcFit <out.bin>");
            return 2;
        }

        var clock = Stopwatch.StartNew();
        Half[] texels = LtcFit.Table();
        File.WriteAllBytes(path, MemoryMarshal.AsBytes(texels.AsSpan()).ToArray());
        Console.WriteLine($"Wrote {texels.Length} halves to {path} in {clock.Elapsed.TotalSeconds:F1} s.");
        return 0;
    }
}
