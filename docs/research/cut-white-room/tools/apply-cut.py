# Tool code of t58-rcd6: applies the diagnosis log and the variant of CutProbe.cs to a plain build copy of main. The
# edits never enter src. Usage: python3 -I apply-cut.py <build copy root>
#
# The log line of each drawn frame, after Render returns (Playback.Capture), with one frame in flight so that the
# device holds only that frame's work, read by the renderer's own read hooks (they wait for the device to be idle):
#   <frame number> draw=<draw index since the last reset> reset=<0|1> size=<render size> faces=<CutProbe.NoteFaces>
#   irr<m>=   map m's irradiance: the mean luminance of its cube's last level (2 x 2 per face, all six faces), the
#             level that the closed form and the readings read, after this draw's prefilter
#   closed=   the closed form's diffuse light: the mean over the surface pixels (g-buffer distance > 0, inside the
#             render size) of the luminance of closedForm(...).diffuse, which this draw's plan pass computed for every
#             traced pixel and wrote into the alpha of the hit light's diffuse layer (unread without
#             TERTIUS_PRECISE_SPECULAR_HISTORY); demodulated and pre-exposed, as the plan has it
#   hit=      the hit light: the mean over the same pixels of the luminance of the hit light's diffuse layer, the
#             light that leaves a pixel toward a diffuse ray (leavingLightOf), as this draw's plan pass wrote it,
#             before the rays of this draw read it; pre-exposed
#   history=  the diffuse history: the mean over the same pixels of the luminance of ScreenTraceImages.ShownDiffuse
#             (the diffuse filter's output, or the slow history without the filter), after this draw's
#             accumulation and filter, the image that the composite reads; demodulated and pre-exposed
#   surface=  the share of the render size's pixels that are surface pixels
import os, shutil, sys

root = sys.argv[1]
here = os.path.dirname(os.path.abspath(__file__))


def edit(rel, old, new):
    path = os.path.join(root, rel)
    s = open(path).read()
    assert s.count(old) == 1, (rel, old[:70])
    open(path, 'w').write(s.replace(old, new))


edit('src/Tertius.Rendering/VulkanRenderer.cs',
     """        EnvironmentDraw environment = EnvironmentDrawFor(snapshot: snapshot, everyFace: pending.RefreshesEveryFace);""",
     """        // PROBE (t58-rcd6): the variant refreshes every face of every map in a draw that resets every history.
        EnvironmentDraw environment = EnvironmentDrawFor(
            snapshot: snapshot,
            everyFace: pending.RefreshesEveryFace || (CutProbe.RefreshAtReset && pending.Reset)
        );""")
edit('src/Tertius.Rendering/VulkanRenderer.cs',
     """        ReadOnlyMemory<FaceMask> faces = map.FacesFor(frame: FrameNumber, served: served, longStep: everyFace);""",
     """        ReadOnlyMemory<FaceMask> faces = map.FacesFor(frame: FrameNumber, served: served, longStep: everyFace);
        CutProbe.NoteFaces(faces.Span, served); // PROBE (t58-rcd6)""")
edit('src/Tertius.Rendering/VulkanRenderer.cs',
     """    internal Half[] ReadEnvironmentFace(int map, CubeFace face, int level, EnvironmentImage image)
    {""",
     """    /// <summary>PROBE (t58-rcd6): writes the cut log's line of the last drawn frame (apply-cut.py has the fields).</summary>
    public void ProbeCutLog()
    {
        if (!CutProbe.Logs || screenTrace is not { } tracer)
            return;
        Extent2D size = context.History.RenderSize;
        var line = new System.Text.StringBuilder(
            FormattableString.Invariant(
                $"{FrameNumber} draw={context.History.DrawIndex} reset={(context.History.Reset ? 1 : 0)} size={size.Width}x{size.Height} faces={CutProbe.Faces}"
            )
        );
        if (environmentMap is { } environment)
            for (int map = 0; map < environment.DrawnFor.Length; map++)
            {
                double sum = 0;
                int count = 0;
                for (int face = 0; face < CubeFaces.Count; face++)
                {
                    Half[] texels = ReadEnvironmentFace(map, (CubeFace)face, IrradianceCube.Levels - 1, EnvironmentImage.Irradiance);
                    for (int i = 0; i + 3 < texels.Length; i += 4, count++)
                        sum += CutProbe.Luminance((double)texels[i], (double)texels[i + 1], (double)texels[i + 2]);
                }
                line.Append(FormattableString.Invariant($" irr{map}={sum / count:G6}"));
            }
        (float[] distance, int dw, int dh) = ReadLevel(image: targets.Distance, layer: 0, mip: 0);
        (float[] hit, int hw, int hh) = ReadLevel(image: tracer.HitLight, layer: 1, mip: 0);
        (float[] history, int sw, int sh) = ReadLevel(image: tracer.ShownDiffuse, layer: 0, mip: 0);
        int dc = distance.Length / (dw * dh), hc = hit.Length / (hw * hh), sc = history.Length / (sw * sh);
        double closedSum = 0, hitSum = 0, historySum = 0;
        int surfaces = 0;
        for (int y = 0; y < size.Height; y++)
        for (int x = 0; x < size.Width; x++)
        {
            if (!(distance[dc * (y * dw + x)] > 0f))
                continue;
            surfaces++;
            int h = hc * (y * hw + x);
            hitSum += CutProbe.Luminance(hit[h], hit[h + 1], hit[h + 2]);
            closedSum += hit[h + 3];
            int s = sc * (y * sw + x);
            historySum += sc >= 3 ? CutProbe.Luminance(history[s], history[s + 1], history[s + 2]) : history[s];
        }
        int n = Math.Max(surfaces, 1);
        line.Append(
            FormattableString.Invariant(
                $" closed={closedSum / n:G6} hit={hitSum / n:G6} history={historySum / n:G6} surface={surfaces / (double)(size.Width * size.Height):F4}"
            )
        );
        CutProbe.Write(line.ToString());
    }

    internal Half[] ReadEnvironmentFace(int map, CubeFace face, int level, EnvironmentImage image)
    {""")
edit('src/Tertius.Host/Scripting/Playback.cs',
     """        renderer.Render(snapshot, alpha: 1f);
        return true;""",
     """        renderer.Render(snapshot, alpha: 1f);
        (renderer as Tertius.Rendering.VulkanRenderer)?.ProbeCutLog(); // PROBE (t58-rcd6)
        return true;""")
edit('src/Tertius.Rendering/Shaders/screen_trace_plan.comp',
     """        storeHitLight(pixel, leavingLightOf(pixel, p.s, p.V, r));""",
     """        LeavingLight probeLeaving = leavingLightOf(pixel, p.s, p.V, r);
        storeHitLight(pixel, probeLeaving);
#ifndef TERTIUS_PRECISE_SPECULAR_HISTORY
        // PROBE (t58-rcd6): the closed form's diffuse light in the diffuse layer's alpha, which nothing reads here.
        vec3 probeClosed = closedForm(withAmbientFootprint(p.s, pixel), p.V).diffuse;
        imageStore(hitLight, ivec3(pixel, KindDiffuse),
                   vec4(probeLeaving.diffuse, dot(probeClosed, vec3(0.2126, 0.7152, 0.0722))));
#endif""")
shutil.copy(os.path.join(here, 'CutProbe.cs'), os.path.join(root, 'src/Tertius.Rendering/Scene/CutProbe.cs'))
print('cut edits applied to', root)
