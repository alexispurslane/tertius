"""Run bounded mainstream specular accumulator controls on the 1:41 moving cockpit.

Tests the user's question directly without changing production defaults:
1. moving-control-trace: unmodified production renderer with
   TERTIUS_REFLECTION_HISTORY_AGREEMENT=control-trace, dumping AccumulationTrace(0)
   layers 0, 2, 4, 5, 6, 8 + G-buffer specular (roughness) + distance on selected
   moving frames, verifying byte-identical output PNGs to moving-normal.
2. moving-mainstream-rough-cap64: existing shared specular accumulator alone
   (no extra RGB history buffer, no spatial spread, exact production historyPlaceOf
   motion/surface/reflected lookup), isolating only the rough-receiver clamp and
   history-shortening policy (roughness >= parameters.reflectedPlaceBelow, cap 64).
   Glossy receivers (< reflectedPlaceBelow = 0.4) stay untouched.
3. moving-mainstream-rough-uncapped: same isolated rough-receiver policy keeping
   production's uncapped PreciseUncapped length (cap = +infinity).

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
    patch_demo_surface,
    patch_script_command,
    build_script,
)

ROOT = Path(__file__).resolve().parents[3]
ACCUMULATE_GLSL = Path('src/Tertius.Rendering/Shaders/accumulate.glsl')
ACCUMULATE_SPATIAL = Path('src/Tertius.Rendering/Shaders/accumulate_spatial.comp')


def patch_renderer_with_trace(text, dump_trace: bool):
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
    if dump_trace:
        insert += '''        if (Environment.GetEnvironmentVariable("TERTIUS_ISSUE5_TRACE_DIR") is { Length: > 0 } traceDir
            && sink != null
            && screenTrace is { } probeTracer
            && probeTracer.Specular.AccumulationTrace(0) is { } traceImage)
        {
            long sd = context.History.DrawIndex;
            if (sd == 32 || sd == 48 || sd == 64 || sd == 80 || sd == 96 || sd == 112 || sd == 127)
            {
                Directory.CreateDirectory(traceDir);
                string stem = Path.Combine(traceDir, sd.ToString("D6"));
                foreach (int layer in (int[])[0, 2, 4, 5, 6, 8])
                {
                    float[] vals = ReadImageLayer(traceImage, layer);
                    File.WriteAllBytes($"{stem}.layer{layer}.f32", System.Runtime.InteropServices.MemoryMarshal.AsBytes(vals.AsSpan()).ToArray());
                }
                byte[] spec = ReadSpecularTarget();
                File.WriteAllBytes($"{stem}.specular.u8", spec);
                float[] dist = ReadDistance();
                File.WriteAllBytes($"{stem}.distance.f32", System.Runtime.InteropServices.MemoryMarshal.AsBytes(dist.AsSpan()).ToArray());
                File.WriteAllText(Path.Combine(traceDir, "size.txt"), $"{traceImage.Extent.Width} {traceImage.Extent.Height}");
            }
        }
'''
    return text.replace(needle, insert + '\n' + needle)


def patch_accumulate_glsl_rough_cap(text, rough_cap: float):
    needle = (
        'float capAt(float roughness)\n'
        '{\n'
        '#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY\n'
        '    if (isinf(parameters.cap))\n'
        '        return parameters.cap;\n'
        '#endif'
    )
    assert text.count(needle) == 1, 'capAt needle not found'
    replacement = (
        'float capAt(float roughness)\n'
        '{\n'
        '#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY\n'
        '    if (parameters.light == SpecularLight && parameters.reflectedPlaceBelow > 0.0\n'
        '        && roughness >= parameters.reflectedPlaceBelow)\n'
        f'        return {rough_cap:.1f};\n'
        '    if (isinf(parameters.cap))\n'
        '        return parameters.cap;\n'
        '#endif'
    )
    return text.replace(needle, replacement)


def patch_accumulate_spatial_skip_rough_clamp(text):
    needle = '    if (historyLength > fastCap)\n    {'
    assert text.count(needle) == 1, 'Step 4 clamp needle not found'
    replacement = (
        '    bool skipRoughSpecularClamp = parameters.light == SpecularLight\n'
        '        && parameters.reflectedPlaceBelow > 0.0\n'
        '        && roughnessAt(pixel) >= parameters.reflectedPlaceBelow;\n'
        '    if (historyLength > fastCap && !skipRoughSpecularClamp)\n'
        '    {'
    )
    return text.replace(needle, replacement)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument(
        'variant',
        choices=[
            'moving-control-trace',
            'moving-mainstream-rough-cap64',
            'moving-mainstream-rough-uncapped',
        ],
    )
    p.add_argument('output', type=Path)
    p.add_argument('--frames', type=int, default=96)
    p.add_argument('--exposure', type=float, default=6.0043726)
    p.add_argument('--content-root', type=Path, default=Path('/Users/alexispurslane/Development/tertius/content'))
    args = p.parse_args()

    os.chdir(ROOT)
    args.output.mkdir(parents=True, exist_ok=False)

    targets = [DEMO_SURFACE, SCRIPT_COMMAND, RENDERER, ACCUMULATE_GLSL, ACCUMULATE_SPATIAL]
    originals = {path: path.read_text() for path in targets}
    changed = dict(originals)

    changed[DEMO_SURFACE] = patch_demo_surface(changed[DEMO_SURFACE])
    changed[SCRIPT_COMMAND] = patch_script_command(changed[SCRIPT_COMMAND])
    changed[RENDERER] = patch_renderer_with_trace(changed[RENDERER], dump_trace=True)

    if args.variant == 'moving-mainstream-rough-cap64':
        changed[ACCUMULATE_GLSL] = patch_accumulate_glsl_rough_cap(changed[ACCUMULATE_GLSL], rough_cap=64.0)
        changed[ACCUMULATE_SPATIAL] = patch_accumulate_spatial_skip_rough_clamp(changed[ACCUMULATE_SPATIAL])
    elif args.variant == 'moving-mainstream-rough-uncapped':
        changed[ACCUMULATE_SPATIAL] = patch_accumulate_spatial_skip_rough_clamp(changed[ACCUMULATE_SPATIAL])

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
                instrumentation='control-trace',
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
        env['TERTIUS_REFLECTION_HISTORY_AGREEMENT'] = 'control-trace'
        env['TERTIUS_ISSUE5_TRACE_DIR'] = str((args.output / 'trace').resolve())

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
