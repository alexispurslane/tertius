# Tool code of t58-rcd6: applies the render scale's probe to a plain build copy, so that a headless film draws under the
# dynamic render scale and logs the controller's requests. The edits never enter src.
#   TERTIUS_X_TARGET_MS=<ms>  the headless renderer draws under FrameTimeTarget(<ms>, 0.5, 1) in place of the fixed 2/3
#   TERTIUS_X_SCALE_LOG=<file> one line per draw, at its Begin:
#     <frame number> cost=<Ordinary|CatchUp> catchup=<the catch-up kind's request> ordinary=<the ordinary kind's request>
#     width=<the draw's render width>
#   The requests are RenderScaleController.RequestedScaleFor of each kind as the draw begins, so the catch-up request in
#   the line of a cut's draw is the request that the cut draws at, which the catch-up draws after the last cut fitted.
# Usage: python3 -I apply-scale.py <build copy root>
import os, sys

root = sys.argv[1]


def edit(rel, old, new):
    path = os.path.join(root, rel)
    s = open(path).read()
    assert s.count(old) == 1, (rel, old[:70])
    open(path, 'w').write(s.replace(old, new))


edit('src/Tertius.Host/ScriptCommand.cs',
     """            Upscaling = new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0),
        };""",
     """            Upscaling = Environment.GetEnvironmentVariable("TERTIUS_X_TARGET_MS") is { Length: > 0 } probeTarget
                ? new Fsr3Upscaling(
                    new FrameTimeTarget(
                        float.Parse(probeTarget, System.Globalization.CultureInfo.InvariantCulture),
                        0.5f,
                        1f
                    ),
                    0
                ) // PROBE (t58-rcd6)
                : new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0),
        };""")
edit('src/Tertius.Rendering/UpscalingSetting.cs',
     """            Fsr3Upscaling { RenderScale: FixedRenderScale } => this,""",
     """            Fsr3Upscaling { RenderScale: FixedRenderScale } => this,
            Fsr3Upscaling { RenderScale: FrameTimeTarget } when Environment.GetEnvironmentVariable("TERTIUS_X_TARGET_MS") is { Length: > 0 } => this, // PROBE (t58-rcd6)""")
# The script's overrides set a fixed scale on every FSR setting, after the options above, so they must keep the target too.
edit('src/Tertius.Host/ScriptCommand.Options.cs',
     """            if (options.Upscaling is Fsr3Upscaling fsr)
                options.Upscaling""",
     """            if (options.Upscaling is Fsr3Upscaling fsr && Environment.GetEnvironmentVariable("TERTIUS_X_TARGET_MS") is not { Length: > 0 }) // PROBE (t58-rcd6)
                options.Upscaling""")
edit('src/Tertius.Rendering/VulkanRenderer.cs',
     """        DrawCost cost = history.CostOf(snapshot: snapshot, step: step, draw: draw, outputSize: targets.OutputExtent);
        return history.Begin(""",
     """        DrawCost cost = history.CostOf(snapshot: snapshot, step: step, draw: draw, outputSize: targets.OutputExtent);
        if (ScaleController is { } probeScale && ProbeScaleLog is { } probeLog) // PROBE (t58-rcd6)
            probeLog.WriteLine(
                FormattableString.Invariant(
                    $"{FrameNumber} cost={cost} catchup={probeScale.RequestedScaleFor(DrawCost.CatchUp):F4} ordinary={probeScale.RequestedScaleFor(DrawCost.Ordinary):F4} width={upscaling.RenderExtent(targets.OutputExtent, probeScale.RequestedScaleFor(cost)).Width}"
                )
            );
        return history.Begin(""")
edit('src/Tertius.Rendering/VulkanRenderer.cs',
     """    private FrameHistory.PendingFrame BeginDraw(RenderSnapshot snapshot, float step, int draw)
    {""",
     """    // PROBE (t58-rcd6): the render scale's log, TERTIUS_X_SCALE_LOG.
    private static readonly System.IO.StreamWriter? ProbeScaleLog =
        Environment.GetEnvironmentVariable("TERTIUS_X_SCALE_LOG") is { Length: > 0 } probePath
            ? new System.IO.StreamWriter(probePath) { AutoFlush = true }
            : null;

    private FrameHistory.PendingFrame BeginDraw(RenderSnapshot snapshot, float step, int draw)
    {""")
print('scale probe applied to', root)
