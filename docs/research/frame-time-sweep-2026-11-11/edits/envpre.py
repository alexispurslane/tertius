# Timing proxies for the environment faces (t2-zcr4), second round. The exposure comes from SWEEP_EXPOSURE, so that a
# changed map does not move the lamps' reach. The kind comes from SWEEP_ENVFACE_EDIT at build time:
#   pre     A depth-only first pass. The surface pipelines compare with GreaterOrEqual. With SWEEP_ENVPRE=1 at run
#           time, each range first draws its solid runs with pipelines that write depth alone (a fragment shader that
#           writes nothing, and a color write mask of 0). Then the range draws as before, and the solid fragments
#           behind the nearest surface fail the depth test before their shader runs.
#   helper  A helper fragment of a 2 x 2 block returns before the light loop.
#   nolight A face shades no light, only the glow.
import os, sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]
kind = os.environ['SWEEP_ENVFACE_EDIT']
common.envexp(root)
P = root + '/src/Tertius.Rendering/Passes/EnvironmentFacePass.cs'
G = root + '/src/Tertius.Rendering/Vulkan/GraphicsPipelineDesc.cs'
F = root + '/src/Tertius.Rendering/Shaders/environment_face.frag'
if kind == 'pre':
    common.sub(G, "    public BlendMode Blend { get; init; }\n",
        "    public BlendMode Blend { get; init; }\n\n    public bool SweepNoColor { get; init; }\n")
    common.sub(G, "            blends[i] = BlendState(Blend);\n",
        "            blends[i] = SweepNoColor ? BlendState(Blend) with { ColorWriteMask = 0 } : BlendState(Blend);\n")
    open(root + '/src/Tertius.Rendering/Shaders/environment_face_depth.frag', 'w').write(
        "#version 460\n\n"
        "// Sweep proxy: the depth-only first pass of an environment face. The color write mask is 0.\n"
        "layout(location = 0) out vec4 outColor;\n"
        "layout(location = 1) out float outInverseDistance;\n\n"
        "void main()\n{\n    outColor = vec4(0.0);\n    outInverseDistance = 0.0;\n}\n")
    common.sub(P, "            DepthCompare = CompareOp.Greater,\n", "            DepthCompare = CompareOp.GreaterOrEqual,\n")
    common.sub(P, "    private readonly SidedPipeline cutoutSurfaces;\n",
        "    private readonly SidedPipeline cutoutSurfaces;\n    private readonly SidedPipeline sweepDepth;\n"
        "    private static readonly bool SweepPre = Environment.GetEnvironmentVariable(\"SWEEP_ENVPRE\") == \"1\";\n")
    common.sub(P, "        try\n        {\n            cutoutSurfaces = SidedPipeline.Create(\n",
        "        sweepDepth = SidedPipeline.Create(device: device, shaders: shaders, desc: desc with\n"
        "        {\n            Name = \"environment faces depth\",\n            FragmentShader = \"environment_face_depth.frag\",\n"
        "            DepthCompare = CompareOp.Greater,\n            SweepNoColor = true,\n        });\n"
        "        try\n        {\n            cutoutSurfaces = SidedPipeline.Create(\n")
    common.sub(P,
        "                GeometryPass.DrawRuns(context: context, runs: runs, solid: surfaces, cutout: cutoutSurfaces);\n",
        "                if (SweepPre)\n"
        "                    GeometryPass.DrawRuns(context: context, runs: runs.With(cutout: true, twoSided: false, commands: default).With(cutout: true, twoSided: true, commands: default), solid: sweepDepth, cutout: sweepDepth);\n"
        "                GeometryPass.DrawRuns(context: context, runs: runs, solid: surfaces, cutout: cutoutSurfaces);\n")
elif kind == 'helper':
    common.sub(F, "    vec3 radiance = directRadiance(shading, glow, V, ShadowMapsHolding, draw.map);\n",
        "    if (gl_HelperInvocation)\n    {\n        outColor = vec4(0.0);\n        outInverseDistance = 0.0;\n        return;\n    }\n"
        "    vec3 radiance = directRadiance(shading, glow, V, ShadowMapsHolding, draw.map);\n")
elif kind == 'nolight':
    common.sub(F, "    vec3 radiance = directRadiance(shading, glow, V, ShadowMapsHolding, draw.map);\n", "    vec3 radiance = glow;\n")
elif kind == 'synthetic':
    # A face's surfaces shade no light. The sky's fullscreen triangle runs one light loop per texel on a made-up
    # surface 3 m from the eye that faces the eye, and adds 1e-30 of it. A fullscreen triangle has no helper
    # fragments, so the faces' time is about the cost of a face that lights each texel once in a separate pass.
    K = root + '/src/Tertius.Rendering/Shaders/environment_sky.frag'
    common.sub(F, "    vec3 radiance = directRadiance(shading, glow, V, ShadowMapsHolding, draw.map);\n", "    vec3 radiance = glow;\n")
    common.sub(K, '#include "sky.glsl"\n', '#include "sky.glsl"\n#include "gbuffer_surface.glsl"\n')
    common.sub(K, "    outColor = vec4(skyRadiance(direction), 1.0);\n",
        "    ShadingInput s;\n    s.position = direction * 3.0;\n    s.baseColor = vec3(0.5);\n    s.normal = -direction;\n"
        "    s.geometricNormal = -direction;\n    s.metallic = 0.0;\n    s.roughness = 0.5;\n    s.reflectance = 0.5;\n"
        "    s.skyFraction = 0.0;\n    s.lobeSkyFraction = 0.0;\n    s.reflectionFootprint = 0.0;\n    s.map = uint(face.right.w);\n"
        "    s.model = ShadingStandard;\n    s.strand = vec3(0.0);\n    vec3 V = -direction;\n"
        "    DirectTerms lit = directLight(s, lobesOf(s, V), V, ShadowMapsHolding, uint(face.right.w));\n"
        "    outColor = vec4(skyRadiance(direction) + (lit.diffuse + lit.specular) * 1e-30, 1.0);\n")
    common.sub(P, "            CullMode = CullModeFlags.None,\n        }.Create(device, shaders);\n",
        "            CullMode = CullModeFlags.None,\n            Constants = constants,\n        }.Create(device, shaders);\n")
    common.sub(P, "                DrawSky(context, draw.Basis);\n", "                DrawSky(context, draw.Basis, draw.Map);\n")
    common.sub(P, "    private void DrawSky(FrameContext context, FaceBasis basis)\n", "    private void DrawSky(FrameContext context, FaceBasis basis, int sweepMap)\n")
    common.sub(P, "            Right = new(basis.Right, 0),\n", "            Right = new(basis.Right, sweepMap),\n")
else:
    raise SystemExit('unknown edit ' + kind)
