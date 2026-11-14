# Timing proxies for the environment faces (t2-zcr4), chosen by the variable SWEEP_ENVFACE_EDIT at build time:
#   draws    The surface pipelines compare with GreaterOrEqual, and each range draws its runs SWEEP_ENVDRAWS times
#            (default 1). The second draw shades only the fragments of the nearest surface, so its time is the cost of
#            a face without depth overdraw. The 100th face also prints the frame's light and command counts.
#   nolight  A face shades no light, only the glow (as edits/envnolight.py).
#   noshadow A face reads no shadow map: ShadowMapsNone keeps the far shadows alone.
import os, sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]
kind = os.environ['SWEEP_ENVFACE_EDIT']
P = root + '/src/Tertius.Rendering/Passes/EnvironmentFacePass.cs'
F = root + '/src/Tertius.Rendering/Shaders/environment_face.frag'
if kind == 'draws':
    common.sub(P, "            DepthCompare = CompareOp.Greater,\n", "            DepthCompare = CompareOp.GreaterOrEqual,\n")
    common.sub(P,
        "                GeometryPass.DrawRuns(context: context, runs: runs, solid: surfaces, cutout: cutoutSurfaces);\n",
        "                for (int sweepDraw = 0; sweepDraw < SweepDraws; sweepDraw++)\n"
        "                    GeometryPass.DrawRuns(context: context, runs: runs, solid: surfaces, cutout: cutoutSurfaces);\n")
    common.sub(P,
        "    public override string Name => \"environment faces\";\n",
        "    public override string Name => \"environment faces\";\n\n"
        "    private static readonly int SweepDraws = int.Parse(Environment.GetEnvironmentVariable(\"SWEEP_ENVDRAWS\") ?? \"1\");\n"
        "    private static int sweepFaces;\n")
    common.sub(P,
        "        uint layer = (uint)(CubeFaces.Count * draw.Map + (int)draw.Face);\n",
        "        uint layer = (uint)(CubeFaces.Count * draw.Map + (int)draw.Face);\n"
        "        if (++sweepFaces is >= 100 and < 106)\n"
        "        {\n"
        "            int kept = 0, shadowed = 0;\n"
        "            foreach (ref readonly PreparedLight light in context.Scene.LightSpan)\n"
        "            {\n"
        "                if (light.Map <= draw.Map) kept++;\n"
        "                if (light.Map <= draw.Map && light.CastsShadow) shadowed++;\n"
        "            }\n"
        "            Console.Error.WriteLine($\"SWEEP face {draw.Face} of map {draw.Map}: lights {context.Scene.LightCount}, kept {kept}, casting {shadowed}, commands far {draw.Far.Count} middle {draw.Middle.Count} near {draw.Near.Count}\");\n"
        "        }\n")
elif kind == 'nolight':
    common.sub(F, "    vec3 radiance = directRadiance(shading, glow, V, ShadowMapsHolding, draw.map);\n", "    vec3 radiance = glow;\n")
elif kind == 'noshadow':
    common.sub(F, "directRadiance(shading, glow, V, ShadowMapsHolding, draw.map)", "directRadiance(shading, glow, V, ShadowMapsNone, draw.map)")
else:
    raise SystemExit('unknown edit ' + kind)
