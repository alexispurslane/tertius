"""Run bounded continuous-motion and fixed-pose multi-phase cockpit diagnostics.

Every engine edit is a temporary patch restored in a finally block.
Run in the Nix dev shell. The final commit changes no engine source.
"""
import argparse
import difflib
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent

DEMO_SURFACE = Path('src/Tertius.Demo/DemoScenes.DemoSurface.cs')
SCRIPT_COMMAND = Path('src/Tertius.Host/ScriptCommand.cs')
RENDERER = Path('src/Tertius.Rendering/VulkanRenderer.cs')
DESC_LAYOUTS = Path('src/Tertius.Rendering/Frame/DescriptorLayouts.cs')
ST_IMAGES = Path('src/Tertius.Rendering/Frame/ScreenTraceImages.cs')
ST_PASS = Path('src/Tertius.Rendering/Passes/ScreenTracePass.cs')
RESOLVE = Path('src/Tertius.Rendering/Shaders/screen_trace_resolve.comp')
ACCUMULATE = Path('src/Tertius.Rendering/Shaders/accumulate_temporal.comp')

CAMERA_COMPONENT = '''
        /// <summary>Temporary research component: deterministic slow repeatable camera motion in cabin space.</summary>
        private sealed class Issue5CockpitCamera : IComponent
        {
            private readonly string mode = Environment.GetEnvironmentVariable("TERTIUS_ISSUE5_CAMERA_MODE") ?? "";
            public int Priority => 0;
            public EventMask Wants => EventMask.Of<Tick>();

            public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
            {
                if (mode.Length == 0 || self.Tick < 87580)
                    return;
                double s;
                if (mode == "move")
                {
                    if (self.Tick < 87581)
                        return;
                    s = Math.Clamp((self.Tick - 87581) / 95.0, 0.0, 1.0);
                }
                else if (mode.StartsWith("fixed:", StringComparison.Ordinal))
                {
                    s = double.Parse(mode.AsSpan(6), System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    return;
                }
                Double3 eye = new Double3(1.60, 1.15, -9.0) + s * new Double3(-0.12, -0.02, -0.08);
                Double3 target = new Double3(0.0, 0.58, -11.55) + s * new Double3(0.32, 0.10, 0.0);
                self.SetTransform(LookingAt(eye, target));
            }
        }
'''


def patch_demo_surface(text):
    needle = (
        '            scene.Add(\n'
        '                "cockpit-camera",\n'
        '                world.Spawn(ViewIn(cabin, LookingAt(new(1.60, 1.15, -9.0), new(0, 0.58, -11.55))))\n'
        '            );'
    )
    assert text.count(needle) == 1, 'cockpit-camera needle not found'
    replacement = (
        '            scene.Add(\n'
        '                "cockpit-camera",\n'
        '                world.Spawn(\n'
        '                    new SpawnDesc\n'
        '                    {\n'
        '                        Frame = cabin,\n'
        '                        Pose = LookingAt(new(1.60, 1.15, -9.0), new(0, 0.58, -11.55)),\n'
        '                        Render = new RenderData(),\n'
        '                        Components = [new Issue5CockpitCamera()],\n'
        '                    }\n'
        '                )\n'
        '            );'
    )
    text = text.replace(needle, replacement)
    end_needle = '                "cockpit-outside-camera",\n                world.Spawn(ViewIn(cabin, LookingAt(new(3.04, 0.51, -15.43), Shuttle.SeatStand + Eye)))\n            );\n        }\n    }'
    assert text.count(end_needle) == 1, 'SpawnCabinCameras end needle not found'
    return text.replace(
        end_needle,
        '                "cockpit-outside-camera",\n'
        '                world.Spawn(ViewIn(cabin, LookingAt(new(3.04, 0.51, -15.43), Shuttle.SeatStand + Eye)))\n'
        '            );\n        }\n' + CAMERA_COMPONENT + '    }',
    )


def patch_script_command(text):
    needle = '        overrides.ApplyTo(rendererOptions);'
    assert text.count(needle) == 1, 'overrides.ApplyTo needle not found'
    insert = (
        '        overrides.ApplyTo(rendererOptions);\n'
        '        if (Environment.GetEnvironmentVariable("TERTIUS_ISSUE5_WARMUP") is { Length: > 0 } warmupText)\n'
        '            rendererOptions.WarmUpFrames = int.Parse(warmupText, System.Globalization.CultureInfo.InvariantCulture);'
    )
    return text.replace(needle, insert)


def patch_renderer(text, read_raw):
    needle = '        LastFrameStats = StatsOf(\n'
    assert text.count(needle) == 1, 'LastFrameStats needle not found'
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
    if read_raw:
        insert += '''        if (Environment.GetEnvironmentVariable("TERTIUS_ISSUE5_RAW") is { Length: > 0 } rawPath
            && screenTrace is { } probeTracer)
        {
            bool allDraws = Environment.GetEnvironmentVariable("TERTIUS_ISSUE5_ALL_DRAWS") == "1";
            long minSample = long.TryParse(Environment.GetEnvironmentVariable("TERTIUS_ISSUE5_MIN_SAMPLE_DRAW"), out var ms) ? ms : 0;
            if ((sink != null || allDraws) && context.History.DrawIndex >= minSample)
            {
                Directory.CreateDirectory(rawPath);
                GpuImage probeImage = probeTracer.Specular.UncoveredDiagnostic(0) ?? throw new InvalidOperationException();
                float[] probeValues = ReadImage(probeImage);
                float[] distValues = ReadDistance();
                string probeName = Path.Combine(rawPath, (history.DrawnFrames - 1).ToString("D6"));
                File.WriteAllBytes(probeName + ".source.f32", System.Runtime.InteropServices.MemoryMarshal.AsBytes(probeValues.AsSpan()).ToArray());
                File.WriteAllBytes(probeName + ".distance.f32", System.Runtime.InteropServices.MemoryMarshal.AsBytes(distValues.AsSpan()).ToArray());
                File.WriteAllText(Path.Combine(rawPath, "size.txt"), $"{probeImage.Extent.Width} {probeImage.Extent.Height}");
            }
        }
'''
    return text.replace(needle, insert + '\n' + needle)


def patch_resolve_raw(text):
    before = '    vec3 specularLight = vec3(0.0);'
    assert text.count(before) == 1
    text = text.replace(before, before + '\n    vec3 probeDiffuseSpecular = vec3(0.0);')
    before = '        specularLight = handOver * diffuse.light.rgb * specularWeightOf(p, lobes, factors, diffuseRay);'
    assert text.count(before) == 1
    text = text.replace(before, before + '\n        probeDiffuseSpecular = specularLight;')
    before = '    imageStore(uncoveredDiagnostic, pixel, vec4(float(seenCode) - 1.0, 0.0, 0.0, 0.0));'
    assert text.count(before) == 1
    return text.replace(
        before,
        '    imageStore(uncoveredDiagnostic, pixel, vec4(factors.specular * probeDiffuseSpecular, handOver));',
    )


def patch_accumulate_keep_diag(text):
    before = (
        '    imageStore(uncoveredDiagnostic, pixel, vec4(imageLoad(uncoveredDiagnostic, pixel).r,\n'
        '        hasSample ? historyLength : -1.0, holdsCutShare ? 1.0 : 0.0, historyLength));'
    )
    assert text.count(before) == 1
    return text.replace(before, '    // Research probe keeps resolve isolated-source RGBA32F output intact.')


def patch_descriptor_layouts_for_accum(text):
    n1 = '    public const uint ScreenTraceBlockGuideBinding = ScreenTraceShareBinding + 1;'
    assert text.count(n1) == 1
    text = text.replace(
        n1,
        n1 + '\n    public const uint ScreenTraceProbePrevBinding = ScreenTraceBlockGuideBinding + 1;'
             '\n    public const uint ScreenTraceProbeCurrBinding = ScreenTraceProbePrevBinding + 1;',
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
        '        bindings[^2] = StorageImage(ScreenTraceProbePrevBinding);\n'
        '        bindings[^1] = StorageImage(ScreenTraceProbeCurrBinding);',
    )
    return text


def patch_st_images_for_accum(text):
    n1 = '    private GpuImage[] lightAges = [];'
    assert text.count(n1) == 1
    text = text.replace(
        n1,
        n1 + '\n    private GpuImage[] probeAccum = [];'
             '\n    public GpuImage ProbeAccum(int parity) => probeAccum[parity];'
             '\n    public GpuImage LastProbeAccum(int parity) => probeAccum[1 - parity];',
    )
    n2 = '        BlockGuide = CreateImage(extent: blocks, format: BlockGuideFormat, layers: 1, name: "screen trace block guide");'
    assert text.count(n2) == 1
    text = text.replace(
        n2,
        n2 + '\n        probeAccum = ['
             '\n            CreateImage(extent: extent, format: Format.R32G32B32A32Sfloat, layers: 1, name: "probe accum 0"),'
             '\n            CreateImage(extent: extent, format: Format.R32G32B32A32Sfloat, layers: 1, name: "probe accum 1"),'
             '\n        ];',
    )
    n3 = '        layouts.WriteStorageImage(set: set, binding: DescriptorLayouts.ScreenTraceBlockGuideBinding, image: BlockGuide);'
    assert text.count(n3) == 1
    text = text.replace(
        n3,
        n3 + '\n        layouts.WriteStorageImage(set: set, binding: DescriptorLayouts.ScreenTraceProbePrevBinding, image: probeAccum[1 - parity]);'
             '\n        layouts.WriteStorageImage(set: set, binding: DescriptorLayouts.ScreenTraceProbeCurrBinding, image: probeAccum[parity]);',
    )
    n4 = '        foreach (GpuImage image in (GpuImage[])[.. Records, HitLight, ReflectionShare, BlockGuide, .. lightAges])'
    assert text.count(n4) == 1
    text = text.replace(
        n4,
        '        foreach (GpuImage image in (GpuImage[])[.. Records, HitLight, ReflectionShare, BlockGuide, .. lightAges, .. probeAccum])',
    )
    n5 = '        lightAges = [];'
    assert text.count(n5) == 1
    text = text.replace(n5, '        lightAges = [];\n        probeAccum = [];')
    return text


def patch_st_pass_for_accum(text):
    needle = (
        '        if (images.LightChangeMask is { } mask)\n'
        '            context.Barriers.Image(mask, ImageUse.StorageCompute, discard: true);\n'
        '        kernel.Record(context, side: 1);'
    )
    assert text.count(needle) == 1
    insert = (
        '        if (images.LightChangeMask is { } mask)\n'
        '            context.Barriers.Image(mask, ImageUse.StorageCompute, discard: true);\n'
        '        context.Barriers.Image(images.LastProbeAccum(context.History.Parity), ImageUse.StorageCompute);\n'
        '        context.Barriers.Image(images.ProbeAccum(context.History.Parity), ImageUse.StorageCompute, discard: true);\n'
        '        kernel.Record(context, side: 1);'
    )
    return text.replace(needle, insert)


def patch_resolve_accum(text, cap, apply_small_spread):
    shared = (HERE / 'spread.glsl').read_text().split('vec3 probeSpread(')[0].replace('PROBE_STEP', '1')
    conn_fn = (HERE / 'connected.glsl').read_text().split('vec3 probeConnectedSpread(')[0]
    accum_fn = (HERE / 'source_accum.glsl').read_text().replace('PROBE_ACCUM_CAP', f'{float(cap):.1f}')
    helper = shared + conn_fn + accum_fn + '\n'
    assert text.count('void main()\n{') == 1
    text = text.replace('void main()\n{', helper + 'void main()\n{')
    sky_needle = '        imageStore(specularSample, pixel, vec4(0.0));'
    assert text.count(sky_needle) == 1
    text = text.replace(
        sky_needle,
        sky_needle + '\n        imageStore(currProbeSourceAccum, pixel, vec4(0.0));',
    )
    needle = '    float hitDistance = diffuse.light.a;'
    assert text.count(needle) == 1
    flag = 'true' if apply_small_spread else 'false'
    return text.replace(
        needle,
        f'    specularLight = probeAccumulateIsolatedSource(pixel, p, factors, specularLight, {flag});\n' + needle,
    )


def patch_resolve_connected(text):
    shared = (HERE / 'spread.glsl').read_text().split('vec3 probeSpread(')[0].replace('PROBE_STEP', '1')
    helper = shared + (HERE / 'connected.glsl').read_text() + '\n'
    assert text.count('void main()\n{') == 1
    text = text.replace('void main()\n{', helper + 'void main()\n{')
    needle = '    float hitDistance = diffuse.light.a;'
    assert text.count(needle) == 1
    return text.replace(
        needle,
        '    specularLight = probeConnectedSpread(pixel, p, factors, specularLight);\n' + needle,
    )


def patch_resolve_no_diffuse_ray_spec(text):
    needle = '        specularLight = handOver * diffuse.light.rgb * specularWeightOf(p, lobes, factors, diffuseRay);'
    assert text.count(needle) == 1
    return text.replace(needle, '        specularLight = vec3(0.0);')


def build_script(frames, extra_run_ticks=0):
    script = json.loads((HERE / 'cockpit.json').read_text())
    if extra_run_ticks > 0:
        script['steps'][-2]['ticks'] += extra_run_ticks
    script['steps'][-1]['frames'] = frames
    script['steps'][-1]['every'] = 1
    return script


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument(
        'variant',
        choices=[
            'moving-normal',
            'moving-source',
            'moving-connected',
            'moving-source-accum',
            'moving-source-accum-spread',
            'moving-no-diffuse-ray-specular',
            'fixed-ref',
        ],
    )
    p.add_argument('output', type=Path)
    p.add_argument('--frames', type=int, default=96)
    p.add_argument('--accum-cap', type=int, default=64)
    p.add_argument('--fixed-s', type=float, default=0.0)
    p.add_argument('--extra-ticks', type=int, default=0)
    p.add_argument('--warmup', type=int, default=159)
    p.add_argument('--min-sample-draw', type=int, default=32)
    p.add_argument('--exposure', type=float, default=6.0043726)
    p.add_argument('--content-root', type=Path, default=Path('/Users/alexispurslane/Development/tertius/content'))
    args = p.parse_args()

    os.chdir(ROOT)
    args.output.mkdir(parents=True, exist_ok=False)

    targets = [DEMO_SURFACE, SCRIPT_COMMAND, RENDERER, DESC_LAYOUTS, ST_IMAGES, ST_PASS, RESOLVE, ACCUMULATE]
    originals = {path: path.read_text() for path in targets}
    changed = dict(originals)

    changed[DEMO_SURFACE] = patch_demo_surface(changed[DEMO_SURFACE])
    changed[SCRIPT_COMMAND] = patch_script_command(changed[SCRIPT_COMMAND])

    reads_raw = args.variant in ('moving-source', 'fixed-ref')
    changed[RENDERER] = patch_renderer(changed[RENDERER], read_raw=reads_raw)

    if reads_raw:
        changed[RESOLVE] = patch_resolve_raw(changed[RESOLVE])
        changed[ACCUMULATE] = patch_accumulate_keep_diag(changed[ACCUMULATE])
    elif args.variant == 'moving-connected':
        changed[RESOLVE] = patch_resolve_connected(changed[RESOLVE])
    elif args.variant in ('moving-source-accum', 'moving-source-accum-spread'):
        changed[DESC_LAYOUTS] = patch_descriptor_layouts_for_accum(changed[DESC_LAYOUTS])
        changed[ST_IMAGES] = patch_st_images_for_accum(changed[ST_IMAGES])
        changed[ST_PASS] = patch_st_pass_for_accum(changed[ST_PASS])
        changed[RESOLVE] = patch_resolve_accum(
            changed[RESOLVE],
            cap=args.accum_cap,
            apply_small_spread=(args.variant == 'moving-source-accum-spread'),
        )
    elif args.variant == 'moving-no-diffuse-ray-specular':
        changed[RESOLVE] = patch_resolve_no_diffuse_ray_spec(changed[RESOLVE])

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
                frames=1 if args.variant == 'fixed-ref' else args.frames,
                every=1,
                accum_cap=args.accum_cap if 'accum' in args.variant else None,
                fixed_s=args.fixed_s if args.variant == 'fixed-ref' else None,
                extra_ticks=args.extra_ticks if args.variant == 'fixed-ref' else 0,
                warmup=args.warmup if args.variant == 'fixed-ref' else 32,
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
        script = build_script(
            frames=1 if args.variant == 'fixed-ref' else args.frames,
            extra_run_ticks=args.extra_ticks if args.variant == 'fixed-ref' else 0,
        )
        script_path = args.output / 'script.json'
        script_path.write_text(json.dumps(script, indent=2) + '\n')

        env = dict(os.environ, TERTIUS_CONTENT_ROOT=str(args.content_root))
        env['TERTIUS_ISSUE5_DRAW_LOG'] = str((args.output / 'draws.jsonl').resolve())
        if args.variant == 'fixed-ref':
            env['TERTIUS_ISSUE5_CAMERA_MODE'] = f'fixed:{args.fixed_s:.17g}'
            env['TERTIUS_ISSUE5_WARMUP'] = str(args.warmup)
            env['TERTIUS_ISSUE5_ALL_DRAWS'] = '1'
            env['TERTIUS_ISSUE5_MIN_SAMPLE_DRAW'] = str(args.min_sample_draw)
        else:
            env['TERTIUS_ISSUE5_CAMERA_MODE'] = 'move'
        if reads_raw:
            env['TERTIUS_ISSUE5_RAW'] = str((args.output / 'raw').resolve())
            env['TERTIUS_REFLECTION_HISTORY_AGREEMENT'] = 'light-age-diagnostic'

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
