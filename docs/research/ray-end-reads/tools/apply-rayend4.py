# Tool code of ter-pdw8, the fourth probe step, applied after apply-rayend3.py on the same build copy (never to src).
#   - The kept samples' hits (<prefix>.hits<k>) and the level histograms (<prefix>.levels) are written only under
#     TERTIUS_PROBE_CENSUS_HITS=1: on bay-sunbeam's keep rectangle at 0.9584 they weigh about 0.9 GB and 0.24 GB per
#     take, and only the lamp hall's cause takes need them.
#   - The census's image dump (TERTIUS_PROBE_CENSUS_DUMP_AT) also writes stand-in (c)'s layers: the value that a hit
#     reads (layers 6 and 7) and the direct mean's state (layer 5, rgb = S / C and alpha = C), for the mixing of two
#     surfaces in one pixel (row 314 of the lamp hall).
# Usage: python3 apply-rayend4.py <build copy root>
import sys
from pathlib import Path

root = Path(sys.argv[1])


def edit(rel, old, new, count=1):
    path = root / rel
    text = path.read_text()
    assert text.count(old) == count, (rel, old[:80], text.count(old))
    path.write_text(text.replace(old, new))


C = 'src/Tertius.Rendering/Scene/RayCensus.cs'
edit(C,
     '''            hitWriters = [.. Keeps.Select((_, k) => new BinaryWriter(File.Create($"{Prefix}.hits{k}")))];
            levels = [.. Enumerable.Range(0, 32).Select(_ => new float[width * height])];
        }
        (float[] hd, int hw, _) = renderer.CensusReadLevel(renderer.ScreenTraceImages.HitLight, 12, 0);
        (float[] hr, _, _) = renderer.CensusReadLevel(renderer.ScreenTraceImages.HitLight, 13, 0);
        float[][] lv = levels!;
        Parallel.For(0, height, row =>''',
     '''            if (Hits)
            {
                hitWriters = [.. Keeps.Select((_, k) => new BinaryWriter(File.Create($"{Prefix}.hits{k}")))];
                levels = [.. Enumerable.Range(0, 32).Select(_ => new float[width * height])];
            }
        }
        (float[] hd, int hw, _) = Hits ? renderer.CensusReadLevel(renderer.ScreenTraceImages.HitLight, 12, 0) : ([], 0, 0);
        (float[] hr, _, _) = Hits ? renderer.CensusReadLevel(renderer.ScreenTraceImages.HitLight, 13, 0) : ([], 0, 0);
        float[][] lv = levels ?? [];
        if (Hits)
        Parallel.For(0, height, row =>''')
edit(C,
     '''                int hat = 4 * (row * hw + column);
                foreach (float v in new float[] { hd[hat], hd[hat + 1], hd[hat + 2], hr[hat], hr[hat + 1], hr[hat + 2] })
                    hitWriters![k].Write(v);''',
     '''                int hat = 4 * (row * hw + column);
                if (Hits)
                    foreach (float v in new float[] { hd[hat], hd[hat + 1], hd[hat + 2], hr[hat], hr[hat + 1], hr[hat + 2] })
                        hitWriters![k].Write(v);''')
edit(C,
     '''        foreach (BinaryWriter hits in hitWriters!)
            hits.Dispose();
        using (var writer = new BinaryWriter(File.Create($"{Prefix}.levels")))
            foreach (float[] array in levels!)
                foreach (float value in array)
                    writer.Write(value);''',
     '''        if (Hits)
        {
            foreach (BinaryWriter hits in hitWriters!)
                hits.Dispose();
            using var writer = new BinaryWriter(File.Create($"{Prefix}.levels"));
            foreach (float[] array in levels!)
                foreach (float value in array)
                    writer.Write(value);
        }''')
edit(C,
     '''    private static float[][]? levels; // PROBE (ter-pdw8): 16 levels of the far diffuse hits, then 16 of the reflection hits''',
     '''    private static float[][]? levels; // PROBE (ter-pdw8): 16 levels of the far diffuse hits, then 16 of the reflection hits
    private static readonly bool Hits = Environment.GetEnvironmentVariable("TERTIUS_PROBE_CENSUS_HITS") == "1";''')
edit(C,
     '''        Dump("hit-diffuse", renderer.CensusReadLevel(images.HitLight, 1, 0));''',
     '''        Dump("hit-diffuse", renderer.CensusReadLevel(images.HitLight, 1, 0));
        Dump("hit-diffuse-c-read", renderer.CensusReadLevel(images.HitLight, 7, 0)); // PROBE (ter-pdw8)
        Dump("hit-diffuse-c-direct-mean", renderer.CensusReadLevel(images.HitLight, 5, 0));''')
print('applied')
