# Tool code of t1-yi11's production check. harness.py <tree>: adds the take harness to a tree that already carries
# tools/renderer-survey/probe.patch. It touches no region rule: TERTIUS_X_FIRST_FILMED and TERTIUS_X_FROZEN (a film of a
# start, and captures of a frozen world one draw at a time), TERTIUS_X_YAW_DEGREES (a fixed turn of the camera) and
# TERTIUS_X_REGION_LOG (each draw appends the bounce light's ring layer of that draw to <folder>/region-ring.log).
import sys
from pathlib import Path

tree = Path(sys.argv[1])


def edit(rel, old, new):
    p = tree / rel
    s = p.read_text()
    assert s.count(old) == 1, (rel, old[:60])
    p.write_text(s.replace(old, new))


edit('src/Tertius.Rendering/Frame/FrameHistory.cs',
     '    private bool IsStill(float step) => rule.Headless && (step == 0 || step >= MeteringStepSeconds);',
     '''    private bool IsStill(float step) => rule.Headless && (step == 0 || step >= MeteringStepSeconds) && !(drawn is null && ProbeFirstFilmed) && !(step == 0 && ProbeFrozen);

    private static readonly bool ProbeFirstFilmed = Environment.GetEnvironmentVariable("TERTIUS_X_FIRST_FILMED") == "1";
    private static readonly bool ProbeFrozen = Environment.GetEnvironmentVariable("TERTIUS_X_FROZEN") == "1";''')
edit('src/Tertius.Rendering/Scene/SceneBatcher.cs',
     '        float tanY = MathF.Tan(fov * 0.5f);\n        return new()',
     '        float tanY = MathF.Tan(fov * 0.5f);\n        rotation *= ProbeTurn;\n        return new()')
edit('src/Tertius.Rendering/Scene/SceneBatcher.cs',
     '    private static CameraView InterpolateCamera(',
     '''    private static readonly Quaternion ProbeTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY,
        (float.TryParse(Environment.GetEnvironmentVariable("TERTIUS_X_YAW_DEGREES"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float degrees) ? degrees : 0f) * MathF.PI / 180f);

    private static CameraView InterpolateCamera(''')
edit('src/Tertius.Rendering/VulkanRenderer.cs',
     '        ProbeCensus();\n',
     '        ProbeCensus();\n        ProbeRegionLog();\n')
edit('src/Tertius.Rendering/VulkanRenderer.cs',
     '    private static UpscalingSetting UpscalingOf(RendererOptions options, bool headless) =>',
     '''    private static readonly string? ProbeRegionLogFolder =
        Environment.GetEnvironmentVariable("TERTIUS_X_REGION_LOG") is { Length: > 0 } regionLogFolder ? regionLogFolder : null;
    private long probeRegionLogDraw;

    private void ProbeRegionLog()
    {
        long draw = probeRegionLogDraw++;
        if (ProbeRegionLogFolder is not { } folder || screenTrace is not { } tracer || tracer.Diffuse.RegionRing(0) is not { } ring)
            return;
        (long drawIndex, _) = LastDraw;
        int layer = (int)(drawIndex % TemporalSignal.RegionRingDraws);
        (float[] values, int width, _) = ReadLevel(image: ring, layer: layer, mip: 0);
        int height = values.Length / 4 / width;
        System.IO.Directory.CreateDirectory(folder);
        using var stream = new System.IO.FileStream(System.IO.Path.Combine(folder, "region-ring.log"), System.IO.FileMode.Append, System.IO.FileAccess.Write);
        using var writer = new System.IO.BinaryWriter(stream);
        writer.Write((int)draw);
        writer.Write((int)drawIndex);
        writer.Write(width);
        writer.Write(height);
        foreach (float value in values)
            writer.Write(value);
    }

    private static UpscalingSetting UpscalingOf(RendererOptions options, bool headless) =>''')
print('harness added')
