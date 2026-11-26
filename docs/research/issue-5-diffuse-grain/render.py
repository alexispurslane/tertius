"""Run one temporary shader experiment, then restore the source, even on failure.

Run in the Nix dev shell. The final commit changes no engine source.
"""
import argparse
import difflib
import json
import os
from pathlib import Path
import subprocess
from raw_probe import RENDERER, ACCUMULATE, patched, source_resolve, source_accumulate

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
COMPOSITE = Path('src/Tertius.Rendering/Shaders/composite_pass.glsl')
RESOLVE = Path('src/Tertius.Rendering/Shaders/screen_trace_resolve.comp')


def modification(variant, original):
    if variant == 'irregular':
        shared = (HERE / 'spread.glsl').read_text().split('vec3 probeSpread(')[0].replace('PROBE_STEP', '1')
        helper = shared + (HERE / 'irregular.glsl').read_text() + '\n'
        original = original.replace('void main()\n{', helper + 'void main()\n{')
        needle = '    float hitDistance = diffuse.light.a;'
        assert original.count(needle) == 1
        return original.replace(needle, '    specularLight = probeIrregularSpread(pixel, p, factors, specularLight);\n' + needle)
    if variant in ('spread-step2', 'spread-step6'):
        step = 2 if variant == 'spread-step2' else 6
        helper = (HERE / 'spread.glsl').read_text().replace('PROBE_STEP', str(step))
        original = original.replace('void main()\n{', helper + 'void main()\n{')
        needle = '    float hitDistance = diffuse.light.a;'
        assert original.count(needle) == 1
        return original.replace(needle, '    specularLight = probeSpread(pixel, p, factors, specularLight);\n' + needle)
    if variant == 'raw-source':
        return source_resolve(original)
    if variant in ('normal', 'raw-normal'):
        return original
    if variant == 'cap-diffuse-ray-specular':
        original = source_resolve(original)
        needle = '        probeDiffuseSpecular = specularLight;'
        cap = '        float probeLevel = luminance(specularLight);\n        specularLight *= min(1.0, PROBE_LIMIT / max(probeLevel, 0.0000001));'
        return original.replace(needle, needle + '\n' + cap)
    if variant == 'no-diffuse-ray-specular':
        needle = '        specularLight = handOver * diffuse.light.rgb * specularWeightOf(p, lobes, factors, diffuseRay);'
        assert original.count(needle) == 1
        return original.replace(needle, '        specularLight = vec3(0.0);')
    if variant == 'zero-specular':
        needle = '    vec3 indirectSpecular = factors.specular * indirect.specular;'
        assert original.count(needle) == 1
        return original.replace(needle, '    if (screenTraceOn)\n        indirect.specular = vec3(0.0);\n' + needle)
    if variant in ('zero-diffuse', 'closed-diffuse'):
        needle = '    vec3 indirectDiffuse = factors.diffuse * indirect.diffuse;'
        value = 'vec3(0.0)' if variant == 'zero-diffuse' else 'closedForm(withAmbientFootprint(s, pixel), V).diffuse'
        assert original.count(needle) == 1
        return original.replace(needle, f'    if (screenTraceOn)\n        indirect.diffuse = {value};\n' + needle)
    raise ValueError(variant)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('variant', choices=['normal', 'zero-diffuse', 'closed-diffuse', 'zero-specular', 'no-diffuse-ray-specular', 'raw-normal', 'cap-diffuse-ray-specular', 'raw-source', 'spread-step2', 'spread-step6', 'irregular'])
    p.add_argument('output', type=Path)
    p.add_argument('--limit', type=float)
    p.add_argument('--frames', type=int, default=120, choices=range(3, 121))
    p.add_argument('--exposure', type=float, required=True)
    p.add_argument('--content-root', type=Path, required=True)
    args = p.parse_args()
    os.chdir(ROOT)
    args.output.mkdir(parents=True, exist_ok=False)
    source = RESOLVE if args.variant in ('no-diffuse-ray-specular', 'cap-diffuse-ray-specular', 'raw-source', 'spread-step2', 'spread-step6', 'irregular') else COMPOSITE
    original = source.read_text()
    changed = modification(args.variant, original)
    if args.variant == 'cap-diffuse-ray-specular':
        assert args.limit is not None and args.limit > 0
        changed = changed.replace('PROBE_LIMIT', str(args.limit))
    renderer_original = RENDERER.read_text()
    renderer_changed = patched(renderer_original) if args.variant in ('raw-normal', 'raw-source', 'cap-diffuse-ray-specular') else renderer_original
    if args.variant in ('raw-source', 'cap-diffuse-ray-specular'):
        renderer_changed = renderer_changed.replace('GpuImage probeImage = probeTracer.Specular.Sample(0);', 'GpuImage probeImage = probeTracer.Specular.UncoveredDiagnostic(0) ?? throw new InvalidOperationException();')
    accumulate_original = ACCUMULATE.read_text()
    accumulate_changed = source_accumulate(accumulate_original) if args.variant in ('raw-source', 'cap-diffuse-ray-specular') else accumulate_original
    patch = ''.join(difflib.unified_diff(original.splitlines(True), changed.splitlines(True),
        fromfile='a/' + str(source), tofile='b/' + str(source)))
    patch += ''.join(difflib.unified_diff(renderer_original.splitlines(True), renderer_changed.splitlines(True), fromfile='a/' + str(RENDERER), tofile='b/' + str(RENDERER)))
    patch += ''.join(difflib.unified_diff(accumulate_original.splitlines(True), accumulate_changed.splitlines(True), fromfile='a/' + str(ACCUMULATE), tofile='b/' + str(ACCUMULATE)))
    (args.output / 'experiment.patch').write_text(patch)
    (args.output / 'settings.json').write_text(json.dumps(dict(
        variant=args.variant, limit=args.limit, exposure=args.exposure, content_root=str(args.content_root),
        base=subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
        note='Temporary research patch; restored after this render.'), indent=2) + '\n')
    try:
        source.write_text(changed)
        if renderer_changed != renderer_original:
            RENDERER.write_text(renderer_changed)
        if accumulate_changed != accumulate_original:
            ACCUMULATE.write_text(accumulate_changed)
        with (args.output / 'build.log').open('w') as log:
            subprocess.run(['dotnet', 'build', 'src/Tertius.Demo', '-c', 'Release'],
                stdout=log, stderr=subprocess.STDOUT, check=True)
        script = json.loads((HERE / 'cockpit.json').read_text())
        script['steps'][-1]['frames'] = args.frames
        script_path = args.output / 'script.json'
        script_path.write_text(json.dumps(script, indent=2) + '\n')
        env = dict(os.environ, TERTIUS_CONTENT_ROOT=str(args.content_root))
        if args.variant in ('raw-normal', 'raw-source', 'cap-diffuse-ray-specular'):
            env['TERTIUS_ISSUE5_RAW'] = str(args.output.resolve() / 'raw')
        if args.variant in ('raw-source', 'cap-diffuse-ray-specular'):
            env['TERTIUS_REFLECTION_HISTORY_AGREEMENT'] = 'light-age-diagnostic'
        with (args.output / 'render.log').open('w') as log:
            subprocess.run(['dotnet', 'run', '--no-build', '--project', 'src/Tertius.Demo', '-c', 'Release',
                '--', '--script', str(script_path), '--headless', '--out', str(args.output / 'frames'),
                '--exposure', str(args.exposure)],
                stdout=log, stderr=subprocess.STDOUT, env=env, check=True)
    finally:
        source.write_text(original)
        if renderer_changed != renderer_original:
            RENDERER.write_text(renderer_original)
        if accumulate_changed != accumulate_original:
            ACCUMULATE.write_text(accumulate_original)


if __name__ == '__main__':
    main()
