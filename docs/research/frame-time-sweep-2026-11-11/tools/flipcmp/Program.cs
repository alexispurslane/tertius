// flipcmp <list file> [heat map folder]: the project's FLIP (tests/Tertius.Rendering.Tests/Flip.cs) of each pair of PNG
// files. Each line of the list is "<label>\t<reference png>\t<test png>". Prints one row per pair: the mean, the 99.9th
// percentile and the largest FLIP value, and the pixels above 0.1, the tolerance of the snapshot tests. With a folder,
// writes the heat map of every pair that has a pixel above 0.1.
using System.Globalization;
using Tertius.Rendering.Tests;

string[] lines = File.ReadAllLines(args[0]);
string? heat = args.Length > 1 ? args[1] : null;
var rows = new string[lines.Length];
Parallel.For(0, lines.Length, new ParallelOptions { MaxDegreeOfParallelism = 3 }, i =>
{
    string[] parts = lines[i].Split('\t');
    Capture a = Capture.Load(parts[1]), b = Capture.Load(parts[2]);
    if (a.Width != b.Width || a.Height != b.Height) { rows[i] = $"{parts[0]}\tsizes differ"; return; }
    FlipMap map = Flip.Compare(a, b);
    float[] sorted = [.. map.Values];
    Array.Sort(sorted);
    float p999 = sorted[(int)(0.999 * (sorted.Length - 1))];
    int above = map.CountAbove(0.1f);
    rows[i] = string.Create(CultureInfo.InvariantCulture, $"{parts[0]}\t{map.Mean:F6}\t{p999:F4}\t{map.Max:F4}\t{above}");
    if (heat is not null && above > 0)
        map.HeatMap().Save(Path.Combine(heat, parts[0].Replace('/', '_') + "-flip.png"));
});
Console.WriteLine("pair\tmean\tp99.9\tmax\tpixels above 0.1");
foreach (string row in rows) Console.WriteLine(row);
