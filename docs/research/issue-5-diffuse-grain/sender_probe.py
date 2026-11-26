"""Sender-side upper-tail & light-pyramid attribution probe on the 1:41 moving cockpit.

Captures unmodified production behavior (verified byte-identical PNGs to moving-normal)
while dumping per-unique-ray and per-receiver attribution buffers plus lightFinest,
hitLight, directDiffuse, emissive, baseColor, specular, shading and distance on 16
settled moving draws (sampleDraw = 48, 52, ..., 108).

Also supports fixed-budget probability-weighted guiding controls (e.g. changing
GuidedShares.Diffuse to 0.50 or 0.75 while keeping the exact 1-ray budget and
exact mixture density) AFTER attribution.

All temporary engine patches are restored in a finally block.
"""
import argparse
import difflib
import json
import os
from pathlib import Path
import subprocess

from moving_render import (
    DEMO_SURFACE,
    SCRIPT_COMMAND,
    RENDERER,
    DESC_LAYOUTS,
    ST_IMAGES,
    ST_PASS,
    RESOLVE,
    patch_demo_surface,
    patch_script_command,
    build_script,
)

ROOT = Path(__file__).resolve().parents[3]
ST_GLSL = Path('src/Tertius.Rendering/Shaders/screen_trace.glsl')
ST_BLOCK = Path('src/Tertius.Rendering/Shaders/screen_trace_block_rays.comp')
ST_OWN = Path('src/Tertius.Rendering/Shaders/screen_trace_own_rays.comp')
GUIDED_SHARES = Path('src/Tertius.Rendering/Shading/GuidedShares.cs')


def patch_descriptor_layouts_for_sender(text):
    n1 = '    public const uint ScreenTraceBlockGuideBinding = ScreenTraceShareBinding + 1;'
    assert text.count(n1) == 1
    text = text.replace(
        n1,
        n1 + '\n    public const uint ScreenTraceProbeBlockBinding = ScreenTraceBlockGuideBinding + 1;'
             '\n    public const uint ScreenTraceProbePixelBinding = ScreenTraceProbeBlockBinding + 1;',
    )
    n2 = '    private const uint ScreenTraceShareImages = 2;'
    assert text.count(n2) == 1
    text = text.replace(n2, '    private const uint ScreenTraceShareImages = 4;')
    n3 = (
        '        bindings[^3] = StorageImage(ScreenTraceHitLightBinding);\n'
        '        bindings[^2] = StorageImage(ScreenTraceShareBinding);\n'
        '        bindings[^1] = StorageImage(ScreenTraceBlockGuideBinding);'
    )
    assert text.count(n3) == 1
    text = text.replace(
        n3,
        '        bindings[^5] = StorageImage(ScreenTraceHitLightBinding);\n'
        '        bindings[^4] = StorageImage(ScreenTraceShareBinding);\n'
        '        bindings[^3] = StorageImage(ScreenTraceBlockGuideBinding);\n'
        '        bindings[^2] = StorageImage(ScreenTraceProbeBlockBinding);\n'
        '        bindings[^1] = StorageImage(ScreenTraceProbePixelBinding);',
    )
    return text


def patch_st_images_for_sender(text):
    n1 = '    private GpuImage[] lightAges = [];'
    assert text.count(n1) == 1
    text = text.replace(
        n1,
        n1 + '\n    public GpuImage? ProbeBlock { get; private set; }'
             '\n    public GpuImage? ProbePixel { get; private set; }',
    )
    n2 = '        BlockGuide = CreateImage(extent: blocks, format: BlockGuideFormat, layers: 1, name: "screen trace block guide");'
    assert text.count(n2) == 1
    text = text.replace(
        n2,
        n2 + '\n        ProbeBlock = CreateImage(extent: blocks, format: Format.R32G32B32A32Sfloat, layers: 3, name: "probe sender block");'
             '\n        ProbePixel = CreateImage(extent: extent, format: Format.R32G32B32A32Sfloat, layers: 5, name: "probe sender pixel");',
    )
    n3 = '        layouts.WriteStorageImage(set: set, binding: DescriptorLayouts.ScreenTraceBlockGuideBinding, image: BlockGuide);'
    assert text.count(n3) == 1
    text = text.replace(
        n3,
        n3 + '\n        if (ProbeBlock is { } pb) layouts.WriteStorageImage(set: set, binding: DescriptorLayouts.ScreenTraceProbeBlockBinding, image: pb);'
             '\n        if (ProbePixel is { } pp) layouts.WriteStorageImage(set: set, binding: DescriptorLayouts.ScreenTraceProbePixelBinding, image: pp);',
    )
    n4 = '        foreach (GpuImage image in (GpuImage[])[.. Records, HitLight, ReflectionShare, BlockGuide, .. lightAges])'
    assert text.count(n4) == 1
    text = text.replace(
        n4,
        '        ProbeBlock?.Dispose(); ProbeBlock = null;\n'
        '        ProbePixel?.Dispose(); ProbePixel = null;\n'
        '        foreach (GpuImage image in (GpuImage[])[.. Records, HitLight, ReflectionShare, BlockGuide, .. lightAges])',
    )
    return text


def patch_st_pass_for_sender(text):
    n1 = '        barriers.Image(images.BlockGuide, ImageUse.StorageCompute, discard: kind == 0);'
    assert text.count(n1) == 1
    text = text.replace(
        n1,
        n1 + '\n        if (images.ProbeBlock is { } pb) barriers.Image(pb, ImageUse.StorageCompute);'
             '\n        if (images.ProbePixel is { } pp) barriers.Image(pp, ImageUse.StorageCompute);',
    )
    n2 = (
        '        if (images.LightChangeMask is { } mask)\n'
        '            context.Barriers.Image(mask, ImageUse.StorageCompute, discard: true);\n'
        '        kernel.Record(context, side: 1);'
    )
    assert text.count(n2) == 1
    text = text.replace(
        n2,
        '        if (images.LightChangeMask is { } mask)\n'
        '            context.Barriers.Image(mask, ImageUse.StorageCompute, discard: true);\n'
        '        if (images.ProbeBlock is { } pb) context.Barriers.Image(pb, ImageUse.StorageCompute);\n'
        '        if (images.ProbePixel is { } pp) context.Barriers.Image(pp, ImageUse.StorageCompute);\n'
        '        kernel.Record(context, side: 1);',
    )
    return text


def patch_st_glsl_for_sender(text):
    n1 = 'layout(set = 3, binding = 29, rgba32f) uniform image2D blockGuide;'
    assert text.count(n1) == 1
    text = text.replace(
        n1,
        n1 + '\nlayout(set = 3, binding = 30, rgba32f) uniform image2DArray probeBlockSender;'
             '\nlayout(set = 3, binding = 31, rgba32f) uniform image2DArray probePixelSender;',
    )
    n2 = '    bool traced;      // false for a direction below the pixel\'s geometric plane, which brings no light'
    assert text.count(n2) == 1
    text = text.replace(
        n2,
        n2 + '\n    ivec2 probeHitPixel;'
             '\n    ivec2 probeTarget;'
             '\n    uint probeFlags;'
             '\n    float probeFade;'
             '\n    float probeMissLum;'
             '\n    float probeLeavingLum;',
    )
    n3 = '    result.traced = dot(result.direction, p.s.geometricNormal) > 0.0;'
    assert text.count(n3) == 1
    text = text.replace(
        n3,
        n3 + '\n    result.probeHitPixel = ivec2(-1, -1);'
             '\n    result.probeTarget = target;'
             '\n    result.probeFlags = (result.traced ? 1u : 0u) | (guided ? 4u : 0u);'
             '\n    result.probeFade = 0.0;'
             '\n    result.probeMissLum = 0.0;'
             '\n    result.probeLeavingLum = 0.0;',
    )
    n4 = '    vec3 missLight = missLightOf(p, pixel, kind, result.direction);\n    result.light = missLight;'
    assert text.count(n4) == 1
    text = text.replace(
        n4,
        n4 + '\n    result.probeMissLum = luminance(missLight);',
    )
    n5 = (
        '    if (findsGuidedDensity)\n'
        '    {\n'
        '        bool counts;\n'
        '        result.guidedDensity = guidedDensityOf(p.origin, result.direction, hitPixel, target, counts);\n'
        '        result.density = mixtureDensity(share, result.lobeDensity, result.guidedDensity);\n'
        '        if (guided && !counts)\n'
        '        {\n'
        '            result.light = vec3(0.0);\n'
        '            return result;\n'
        '        }\n'
        '    }'
    )
    assert text.count(n5) == 1
    text = text.replace(
        n5,
        '    result.probeFlags |= 2u;\n'
        '    result.probeHitPixel = hitPixel;\n'
        '    if (findsGuidedDensity)\n'
        '    {\n'
        '        bool counts;\n'
        '        result.guidedDensity = guidedDensityOf(p.origin, result.direction, hitPixel, target, counts);\n'
        '        result.density = mixtureDensity(share, result.lobeDensity, result.guidedDensity);\n'
        '        if (guided && counts)\n'
        '            result.probeFlags |= 8u;\n'
        '        if (guided && !counts)\n'
        '        {\n'
        '            result.light = vec3(0.0);\n'
        '            return result;\n'
        '        }\n'
        '    }',
    )
    n6 = '    vec4 leaving = imageLoad(hitLight, ivec3(hitPixel, kind));\n    result.light = mix(missLight, leaving.rgb, fade);'
    assert text.count(n6) == 1
    text = text.replace(
        n6,
        n6 + '\n    result.probeFade = fade;\n    result.probeLeavingLum = luminance(leaving.rgb);',
    )
    return text


def patch_block_rays_for_sender(text):
    n = (
        '        TracedRay ray = traceFrom(chosen, block, p, rayKind);\n'
        '        record = recordOf(ray);'
    )
    assert text.count(n) == 1
    repl = (
        '        TracedRay ray = traceFrom(chosen, block, p, rayKind);\n'
        '        record = recordOf(ray);\n'
        '        if (rayKind == KindDiffuse)\n'
        '        {\n'
        '            imageStore(probeBlockSender, ivec3(block, 0), vec4(vec2(ray.probeHitPixel), vec2(ray.probeTarget)));\n'
        '            imageStore(probeBlockSender, ivec3(block, 1), vec4(float(ray.probeFlags), ray.probeFade, ray.lobeDensity, ray.guidedDensity));\n'
        '            imageStore(probeBlockSender, ivec3(block, 2), vec4(ray.probeMissLum, ray.probeLeavingLum, ray.density, ray.distance));\n'
        '        }'
    )
    return text.replace(n, repl)


def patch_own_rays_for_sender(text):
    n = '        RayRecord record = recordOf(traceFrom(pixel, pixel, tracingPixelAt(pixel), rayKind));'
    assert text.count(n) == 1
    repl = (
        '        TracedRay ray = traceFrom(pixel, pixel, tracingPixelAt(pixel), rayKind);\n'
        '        RayRecord record = recordOf(ray);\n'
        '        if (rayKind == KindDiffuse)\n'
        '        {\n'
        '            imageStore(probePixelSender, ivec3(pixel, 0), vec4(vec2(ray.probeHitPixel), vec2(ray.probeTarget)));\n'
        '            imageStore(probePixelSender, ivec3(pixel, 1), vec4(float(ray.probeFlags | 16u), ray.probeFade, ray.lobeDensity, ray.guidedDensity));\n'
        '            imageStore(probePixelSender, ivec3(pixel, 2), vec4(ray.probeMissLum, ray.probeLeavingLum, ray.density, ray.distance));\n'
        '        }'
    )
    return text.replace(n, repl)


def patch_resolve_for_sender(text):
    n1 = '        imageStore(specularSample, pixel, vec4(0.0));'
    assert text.count(n1) == 1
    text = text.replace(
        n1,
        n1 + '\n        imageStore(probePixelSender, ivec3(pixel, 3), vec4(0.0));'
             '\n        imageStore(probePixelSender, ivec3(pixel, 4), vec4(0.0));',
    )
    n2 = '    vec3 specularLight = vec3(0.0);'
    assert text.count(n2) == 1
    text = text.replace(
        n2,
        n2 + '\n    vec3 probeDiffSpecOut = vec3(0.0);'
             '\n    float probeCappedW = 0.0;'
             '\n    float probeUncappedW = 0.0;',
    )
    n3 = '        specularLight = handOver * diffuse.light.rgb * specularWeightOf(p, lobes, factors, diffuseRay);'
    assert text.count(n3) == 1
    text = text.replace(
        n3,
        '        vec3 uncappedW = uncappedSpecularWeightOf(p, lobes, factors, diffuseRay.direction, diffuseRay.density);\n'
        '        vec3 cappedW = min(uncappedW, vec3(MostRayWeight));\n'
        '        specularLight = handOver * diffuse.light.rgb * cappedW;\n'
        '        probeDiffSpecOut = factors.specular * specularLight;\n'
        '        probeCappedW = max(cappedW.r, max(cappedW.g, cappedW.b));\n'
        '        probeUncappedW = max(uncappedW.r, max(uncappedW.g, uncappedW.b));',
    )
    n4 = '    imageStore(specularSample, pixel, vec4(min(specularLight, vec3(MaxRadiance)), hitDistance));'
    assert text.count(n4) == 1
    text = text.replace(
        n4,
        n4 + '\n    imageStore(probePixelSender, ivec3(pixel, 3), vec4(probeDiffSpecOut, handOver));'
             '\n    imageStore(probePixelSender, ivec3(pixel, 4), vec4(diffuseRay.brings ? luminance(diffuse.light.rgb) : 0.0, probeCappedW, probeUncappedW, (plan & PlanOwnDiffuse) != 0u ? 1.0 : 0.0));',
    )
    return text


def patch_renderer_for_sender(text, dump_sender: bool):
    needle = '        LastFrameStats = StatsOf(\n'
    assert text.count(needle) == 1
    insert = '''        if (Environment.GetEnvironmentVariable("TERTIUS_ISSUE5_DRAW_LOG") is { Length: > 0 } drawLogPath)
        {
            if (Path.GetDirectoryName(drawLogPath) is { Length: > 0 } drawDir)
                Directory.CreateDirectory(drawDir);
            CameraView cam = LastScene.Camera;
            Double3 eye = snapshot.OriginInFrame + new Double3(cam.Offset.X, cam.Offset.Y, cam.Offset.Z);
            Vector2 jitter = context.History.Jitter;
            var rec = new
            {
                globalDraw = history.DrawnFrames - 1,
                sampleDraw = context.History.DrawIndex,
                reset = context.History.Reset,
                captured = sink != null,
                tick = snapshot.Tick,
                width = cam.Width,
                height = cam.Height,
                eye = new double[] { eye.X, eye.Y, eye.Z },
                forward = new float[] { cam.Forward.X, cam.Forward.Y, cam.Forward.Z },
                rightTan = new float[] { cam.Right.X * cam.TanHalfX, cam.Right.Y * cam.TanHalfX, cam.Right.Z * cam.TanHalfX },
                upTan = new float[] { cam.Up.X * cam.TanHalfY, cam.Up.Y * cam.TanHalfY, cam.Up.Z * cam.TanHalfY },
                jitter = new float[] { jitter.X, jitter.Y },
                exposureStops = Exposure.Latest,
            };
            File.AppendAllText(drawLogPath, System.Text.Json.JsonSerializer.Serialize(rec) + "\\n");
        }
'''
    if dump_sender:
        insert += '''        if (Environment.GetEnvironmentVariable("TERTIUS_ISSUE5_SENDER_DIR") is { Length: > 0 } senderDir
            && sink != null
            && screenTrace is { } probeTracer
            && probeTracer.ProbeBlock is { } probeBlock
            && probeTracer.ProbePixel is { } probePixel)
        {
            long sd = context.History.DrawIndex;
            if (sd >= 48 && sd <= 108 && (sd - 48) % 4 == 0)
            {
                Directory.CreateDirectory(senderDir);
                string stem = Path.Combine(senderDir, sd.ToString("D6"));
                foreach (int layer in (int[])[0, 1, 2])
                {
                    float[] bvals = ReadImageLayer(probeBlock, layer);
                    File.WriteAllBytes($"{stem}.block{layer}.f32", System.Runtime.InteropServices.MemoryMarshal.AsBytes(bvals.AsSpan()).ToArray());
                }
                foreach (int layer in (int[])[0, 1, 2, 3, 4])
                {
                    float[] pvals = ReadImageLayer(probePixel, layer);
                    File.WriteAllBytes($"{stem}.pixel{layer}.f32", System.Runtime.InteropServices.MemoryMarshal.AsBytes(pvals.AsSpan()).ToArray());
                }
                float[] lf = ReadImage(probeTracer.LightPyramidImages.Finest);
                File.WriteAllBytes($"{stem}.lightFinest.f32", System.Runtime.InteropServices.MemoryMarshal.AsBytes(lf.AsSpan()).ToArray());
                float[] hl = ReadImageLayer(probeTracer.HitLight, 1);
                File.WriteAllBytes($"{stem}.hitLightDiffuse.f32", System.Runtime.InteropServices.MemoryMarshal.AsBytes(hl.AsSpan()).ToArray());
                if (sd == 48 || sd == 80)
                {
                    float[] dd = ReadImage(targets.DirectDiffuse);
                    File.WriteAllBytes($"{stem}.directDiffuse.f32", System.Runtime.InteropServices.MemoryMarshal.AsBytes(dd.AsSpan()).ToArray());
                    float[] em = ReadImage(targets.Emissive);
                    File.WriteAllBytes($"{stem}.emissive.f32", System.Runtime.InteropServices.MemoryMarshal.AsBytes(em.AsSpan()).ToArray());
                    byte[] sh = ReadShading();
                    File.WriteAllBytes($"{stem}.shading.u8", sh);
                    byte[] sp = ReadSpecularTarget();
                    File.WriteAllBytes($"{stem}.specular.u8", sp);
                    float[] di = ReadDistance();
                    File.WriteAllBytes($"{stem}.distance.f32", System.Runtime.InteropServices.MemoryMarshal.AsBytes(di.AsSpan()).ToArray());
                }
                File.WriteAllText(Path.Combine(senderDir, "size.txt"),
                    $"{probePixel.Extent.Width} {probePixel.Extent.Height} {probeBlock.Extent.Width} {probeBlock.Extent.Height}");
            }
        }
'''
    return text.replace(needle, insert + '\n' + needle)


def patch_guided_diffuse_share(text, share: float):
    needle = '    public const float Diffuse = 0.25f;'
    assert text.count(needle) == 1
    return text.replace(needle, f'    public const float Diffuse = {share:.4f}f;')


def patch_st_glsl_tight_guided_box(text):
    n1 = 'const float TargetDepthShare = 0.05;'
    assert text.count(n1) == 1
    text = text.replace(n1, 'const float TargetDepthShare = 0.01;')
    n2 = (
        '    for (int y = -1; y <= 1; y++)\n'
        '        for (int x = -1; x <= 1; x++)'
    )
    assert text.count(n2) == 1
    text = text.replace(
        n2,
        '    for (int y = -2; y <= 2; y++)\n'
        '        for (int x = -2; x <= 2; x++)',
    )
    return text


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument(
        'variant',
        choices=['sender-baseline', 'guided-diffuse-050', 'guided-diffuse-075', 'guided-diffuse-075-tight', 'guided-diffuse-000'],
    )
    p.add_argument('output', type=Path)
    p.add_argument('--frames', type=int, default=96)
    p.add_argument('--exposure', type=float, default=6.0043726)
    p.add_argument('--content-root', type=Path, default=Path('/Users/alexispurslane/Development/tertius/content'))
    args = p.parse_args()

    os.chdir(ROOT)
    args.output.mkdir(parents=True, exist_ok=False)

    targets = [
        DEMO_SURFACE,
        SCRIPT_COMMAND,
        RENDERER,
        DESC_LAYOUTS,
        ST_IMAGES,
        ST_PASS,
        ST_GLSL,
        ST_BLOCK,
        ST_OWN,
        RESOLVE,
        GUIDED_SHARES,
    ]
    originals = {path: path.read_text() for path in targets}
    changed = dict(originals)

    changed[DEMO_SURFACE] = patch_demo_surface(changed[DEMO_SURFACE])
    changed[SCRIPT_COMMAND] = patch_script_command(changed[SCRIPT_COMMAND])
    dump_sender = args.variant == 'sender-baseline'
    changed[RENDERER] = patch_renderer_for_sender(changed[RENDERER], dump_sender=dump_sender)

    if dump_sender:
        changed[DESC_LAYOUTS] = patch_descriptor_layouts_for_sender(changed[DESC_LAYOUTS])
        changed[ST_IMAGES] = patch_st_images_for_sender(changed[ST_IMAGES])
        changed[ST_PASS] = patch_st_pass_for_sender(changed[ST_PASS])
        changed[ST_GLSL] = patch_st_glsl_for_sender(changed[ST_GLSL])
        changed[ST_BLOCK] = patch_block_rays_for_sender(changed[ST_BLOCK])
        changed[ST_OWN] = patch_own_rays_for_sender(changed[ST_OWN])
        changed[RESOLVE] = patch_resolve_for_sender(changed[RESOLVE])
    elif args.variant == 'guided-diffuse-050':
        changed[GUIDED_SHARES] = patch_guided_diffuse_share(changed[GUIDED_SHARES], 0.50)
    elif args.variant == 'guided-diffuse-075':
        changed[GUIDED_SHARES] = patch_guided_diffuse_share(changed[GUIDED_SHARES], 0.75)
    elif args.variant == 'guided-diffuse-075-tight':
        changed[GUIDED_SHARES] = patch_guided_diffuse_share(changed[GUIDED_SHARES], 0.75)
        changed[ST_GLSL] = patch_st_glsl_tight_guided_box(changed[ST_GLSL])
    elif args.variant == 'guided-diffuse-000':
        changed[GUIDED_SHARES] = patch_guided_diffuse_share(changed[GUIDED_SHARES], 0.00)

    patch = ''
    for path in targets:
        if changed[path] != originals[path]:
            patch += ''.join(
                difflib.unified_diff(
                    originals[path].splitlines(True),
                    changed[path].splitlines(True),
                    fromfile='a/' + str(path),
                    tofile='b/' + str(path),
                )
            )
    (args.output / 'experiment.patch').write_text(patch)
    (args.output / 'settings.json').write_text(
        json.dumps(
            dict(
                variant=args.variant,
                frames=args.frames,
                every=1,
                exposure=args.exposure,
                content_root=str(args.content_root),
                base=subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
                note='Temporary research patch; restored after this render.',
            ),
            indent=2,
        )
        + '\n'
    )

    try:
        for path in targets:
            if changed[path] != originals[path]:
                path.write_text(changed[path])
        with (args.output / 'build.log').open('w') as log:
            subprocess.run(
                ['dotnet', 'build', 'src/Tertius.Demo', '-c', 'Release'],
                stdout=log,
                stderr=subprocess.STDOUT,
                check=True,
            )
        script = build_script(frames=args.frames)
        script_path = args.output / 'script.json'
        script_path.write_text(json.dumps(script, indent=2) + '\n')

        env = dict(os.environ, TERTIUS_CONTENT_ROOT=str(args.content_root))
        env['TERTIUS_ISSUE5_DRAW_LOG'] = str((args.output / 'draws.jsonl').resolve())
        env['TERTIUS_ISSUE5_CAMERA_MODE'] = 'move'
        if dump_sender:
            env['TERTIUS_ISSUE5_SENDER_DIR'] = str((args.output / 'sender').resolve())
        if args.variant == 'guided-diffuse-000':
            env['TERTIUS_GUIDED_RAYS'] = 'on'  # keep reflection guiding intact, only diffuse share = 0

        with (args.output / 'render.log').open('w') as log:
            subprocess.run(
                [
                    'dotnet',
                    'run',
                    '--no-build',
                    '--project',
                    'src/Tertius.Demo',
                    '-c',
                    'Release',
                    '--',
                    '--script',
                    str(script_path),
                    '--headless',
                    '--out',
                    str(args.output / 'frames'),
                    '--exposure',
                    str(args.exposure),
                ],
                stdout=log,
                stderr=subprocess.STDOUT,
                env=env,
                check=True,
            )
    finally:
        for path in targets:
            if changed[path] != originals[path]:
                path.write_text(originals[path])


if __name__ == '__main__':
    main()
