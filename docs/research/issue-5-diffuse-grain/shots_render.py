"""Render matched true-film Blue Danube shot slices for issue #5 cross-shot validation.

Extracts exact tick-matched slices from scripts/blue-danube.json (preserving every prior
post and simulation tick), supports auto-exposure metering and fixed-exposure A/B runs
(baseline, candidate, no-diffuse-ray-specular, zero-diffuse, source-accum), and restores
all temporary engine patches in a finally block.
"""
import argparse
import difflib
import json
import os
from pathlib import Path
import subprocess

from moving_render import (
    RENDERER,
    DESC_LAYOUTS,
    ST_IMAGES,
    ST_PASS,
    RESOLVE,
    patch_renderer,
    patch_descriptor_layouts_for_accum,
    patch_st_images_for_accum,
    patch_st_pass_for_accum,
    patch_resolve_accum,
    patch_resolve_no_diffuse_ray_spec,
)

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
COMPOSITE = Path('src/Tertius.Rendering/Shaders/composite_pass.glsl')
BLUE_DANUBE = Path('scripts/blue-danube.json')


def patch_composite_zero_diffuse(text):
    needle = '    vec3 indirectDiffuse = factors.diffuse * indirect.diffuse;'
    assert text.count(needle) == 1
    return text.replace(needle, '    if (screenTraceOn)\n        indirect.diffuse = vec3(0.0);\n' + needle)


def prefix_steps_to_tick(full_script, target_tick):
    """Build run+post steps from blue-danube.json up to exact target_tick (preserving all posts)."""
    steps = []
    tick = 0
    for st in full_script['steps']:
        d = st['do']
        if d == 'post':
            if tick <= target_tick:
                steps.append(st)
        elif d == 'run':
            n = st['ticks']
            if tick + n <= target_tick:
                steps.append({'do': 'run', 'ticks': n})
                tick += n
            else:
                rem = target_tick - tick
                if rem > 0:
                    steps.append({'do': 'run', 'ticks': rem})
                    tick += rem
                break
        elif d == 'film':
            n = st['frames'] * st['every']
            if tick + n <= target_tick:
                steps.append({'do': 'run', 'ticks': n})
                tick += n
            else:
                rem = target_tick - tick
                if rem > 0:
                    steps.append({'do': 'run', 'ticks': rem})
                    tick += rem
                break
        elif d == 'outlines':
            if tick <= target_tick:
                steps.append(st)
    assert tick == target_tick, f'reached tick {tick}, wanted {target_tick}'
    # Merge adjacent run steps for compactness
    merged = []
    for st in steps:
        if st['do'] == 'run' and merged and merged[-1]['do'] == 'run':
            merged[-1] = {'do': 'run', 'ticks': merged[-1]['ticks'] + st['ticks']}
        else:
            merged.append(st)
    return merged


def make_shot_script(shot_name):
    full = json.loads(BLUE_DANUBE.read_text())
    if shot_name == 'shot1-doorway':
        # terminal ends at tick 461 (0:12.68), cuts to follow-camera (pad) at 0:12.68..0:14.18 (0:13 is pad frame 19)
        steps = prefix_steps_to_tick(full, 431)
        steps += [
            {'do': 'camera', 'entity': 'terminal-camera'},
            {'do': 'film', 'name': 's1a', 'frames': 30, 'every': 1},
            {'do': 'camera', 'entity': 'follow-camera'},
            {'do': 'film', 'name': 's1b', 'frames': 90, 'every': 1},
        ]
        meta = dict(
            shot=shot_name,
            film_timestamps='0:12.18..0:12.68 (terminal-camera, 30f) | CUT at 0:12.68 | 0:12.68..0:14.18 (follow-camera, 90f, 0:13=s1b-000019)',
            mode='ordinary-motion (every=1, 60 Hz) with real camera cut',
        )
    elif shot_name == 'shot2-cockpit-dash':
        # cabin ends at tick 1371 (0:27.85), cuts to seat-camera (seated, 45f, 0:27.85..0:28.60)
        steps = prefix_steps_to_tick(full, 1291)
        steps += [
            {'do': 'camera', 'entity': 'follow-camera'},
            {'do': 'film', 'name': 's2a', 'frames': 80, 'every': 1},
            {'do': 'camera', 'entity': 'seat-camera'},
            {'do': 'film', 'name': 's2b', 'frames': 45, 'every': 1},
        ]
        meta = dict(
            shot=shot_name,
            film_timestamps='0:26.52..0:27.85 (follow-camera, 80f, 0:27=s2a-000029) | CUT at 0:27.85 | 0:27.85..0:28.60 (seat-camera, 45f, 0:28=s2b-000009)',
            mode='ordinary-motion (every=1, 60 Hz) with real camera cut',
        )
    elif shot_name == 'shot3a-arrival-early':
        # arrival starts at tick 289233 (4:10.30, every=4). 4:14.48 is frame 251 (tick 290237).
        # Capture frames 205..284 (80 frames, ticks 290053..290373, 4:13.72..4:15.05, 4:14.48 = frame 46)
        steps = prefix_steps_to_tick(full, 290053)
        steps += [
            {'do': 'camera', 'entity': 'cockpit-camera'},
            {'do': 'film', 'name': 's3a', 'frames': 80, 'every': 4},
        ]
        meta = dict(
            shot=shot_name,
            film_timestamps='4:13.72..4:15.05 (cockpit-camera, every=4 time-lapse, 80f, 4:14.48=s3a-000046)',
            mode='true-film time-lapse (every=4, 0.0667 s/frame < 0.09 s still threshold, continuous draws)',
        )
    elif shot_name == 'shot3b-arrival-late':
        # arrival 4:19.82 is frame 571 (tick 291517).
        # Capture frames 525..604 (80 frames, ticks 291333..291653, 4:19.05..4:20.38, 4:19.82 = frame 46)
        steps = prefix_steps_to_tick(full, 291333)
        steps += [
            {'do': 'camera', 'entity': 'cockpit-camera'},
            {'do': 'film', 'name': 's3b', 'frames': 80, 'every': 4},
        ]
        meta = dict(
            shot=shot_name,
            film_timestamps='4:19.05..4:20.38 (cockpit-camera, every=4 time-lapse, 80f, 4:19.82=s3b-000046)',
            mode='true-film time-lapse (every=4, 0.0667 s/frame < 0.09 s still threshold, continuous draws)',
        )
    elif shot_name == 'shot3c-arrival-1x':
        # Ordinary 60 Hz motion comparison around 4:19.82 (tick 291517): ticks 291457..291577 (120f, every=1)
        steps = prefix_steps_to_tick(full, 291457)
        steps += [
            {'do': 'camera', 'entity': 'cockpit-camera'},
            {'do': 'film', 'name': 's3c', 'frames': 120, 'every': 1},
        ]
        meta = dict(
            shot=shot_name,
            film_timestamps='ticks 291457..291577 centered on 4:19.82 (cockpit-camera, every=1 ordinary 60 Hz, 120f, 4:19.82=s3c-000060)',
            mode='ordinary-motion (every=1, 60 Hz) control matched to 4:19.82',
        )
    elif shot_name == 'shot4-berth':
        # work ends at tick 316952 (7:25.42), cuts to deck-camera (every=1). 7:27.00 is deck frame 95 (tick 317047).
        steps = prefix_steps_to_tick(full, 316922)
        steps += [
            {'do': 'camera', 'entity': 'work-camera'},
            {'do': 'film', 'name': 's4a', 'frames': 30, 'every': 1},
            {'do': 'camera', 'entity': 'deck-camera'},
            {'do': 'film', 'name': 's4b', 'frames': 130, 'every': 1},
        ]
        meta = dict(
            shot=shot_name,
            film_timestamps='7:24.92..7:25.42 (work-camera, 30f) | CUT at 7:25.42 | 7:25.42..7:27.58 (deck-camera, 130f, 7:27=s4b-000095)',
            mode='ordinary-motion (every=1, 60 Hz) with real camera cut',
        )
    else:
        raise ValueError(shot_name)
    return {'scene': 'demo-surface', 'window': {'width': 1280, 'height': 720}, 'steps': steps}, meta


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument(
        'shot',
        choices=['shot1-doorway', 'shot2-cockpit-dash', 'shot3a-arrival-early', 'shot3b-arrival-late', 'shot3c-arrival-1x', 'shot4-berth', 'batch'],
    )
    p.add_argument(
        'variant',
        choices=['meter', 'baseline', 'candidate', 'candidate-cap32', 'source-accum', 'no-diffuse-ray-specular', 'zero-diffuse'],
    )
    p.add_argument('output', type=Path)
    p.add_argument('--exposure', type=float, default=None)
    p.add_argument('--batch-spec', type=str, default=None, help='JSON list of [shot, exposure] pairs when shot=batch')
    p.add_argument('--content-root', type=Path, default=Path('/Users/alexispurslane/Development/tertius/content'))
    args = p.parse_args()

    os.chdir(ROOT)
    jobs = (
        [(s, e, args.output / s / args.variant) for s, e in json.loads(args.batch_spec)]
        if args.shot == 'batch'
        else [(args.shot, args.exposure, args.output)]
    )
    for _, _, out_dir in jobs:
        out_dir.mkdir(parents=True, exist_ok=False)

    targets = [RENDERER, DESC_LAYOUTS, ST_IMAGES, ST_PASS, RESOLVE, COMPOSITE]
    originals = {path: path.read_text() for path in targets}
    changed = dict(originals)

    changed[RENDERER] = patch_renderer(changed[RENDERER], read_raw=False)

    if args.variant in ('candidate', 'candidate-cap32', 'source-accum'):
        cap = 32 if args.variant == 'candidate-cap32' else 64
        changed[DESC_LAYOUTS] = patch_descriptor_layouts_for_accum(changed[DESC_LAYOUTS])
        changed[ST_IMAGES] = patch_st_images_for_accum(changed[ST_IMAGES])
        changed[ST_PASS] = patch_st_pass_for_accum(changed[ST_PASS])
        changed[RESOLVE] = patch_resolve_accum(
            changed[RESOLVE],
            cap=cap,
            apply_small_spread=(args.variant != 'source-accum'),
        )
    elif args.variant == 'no-diffuse-ray-specular':
        changed[RESOLVE] = patch_resolve_no_diffuse_ray_spec(changed[RESOLVE])
    elif args.variant == 'zero-diffuse':
        changed[COMPOSITE] = patch_composite_zero_diffuse(changed[COMPOSITE])

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
    head_sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()

    try:
        for path in targets:
            if changed[path] != originals[path]:
                path.write_text(changed[path])
        build_log = jobs[0][2] / 'build.log'
        with build_log.open('w') as log:
            subprocess.run(
                ['dotnet', 'build', 'src/Tertius.Demo', '-c', 'Release'],
                stdout=log,
                stderr=subprocess.STDOUT,
                check=True,
            )
        build_text = build_log.read_text()
        for shot_name, exp, out_dir in jobs:
            (out_dir / 'experiment.patch').write_text(patch)
            (out_dir / 'build.log').write_text(build_text)
            script, meta = make_shot_script(shot_name)
            script_path = out_dir / 'script.json'
            script_path.write_text(json.dumps(script, indent=2) + '\n')
            (out_dir / 'settings.json').write_text(
                json.dumps(
                    dict(
                        **meta,
                        variant=args.variant,
                        exposure=exp,
                        content_root=str(args.content_root),
                        base=head_sha,
                        note='Temporary research patch; restored after this render.',
                    ),
                    indent=2,
                )
                + '\n'
            )
            env = dict(os.environ, TERTIUS_CONTENT_ROOT=str(args.content_root))
            env['TERTIUS_ISSUE5_DRAW_LOG'] = str((out_dir / 'draws.jsonl').resolve())
            cmd = [
                'dotnet', 'run', '--no-build', '--project', 'src/Tertius.Demo', '-c', 'Release',
                '--', '--script', str(script_path), '--headless', '--out', str(out_dir / 'frames'),
            ]
            if exp is not None:
                cmd += ['--exposure', str(exp)]
            print(f'=== RENDERING {shot_name} / {args.variant} (exposure={exp}) ===', flush=True)
            with (out_dir / 'render.log').open('w') as log:
                subprocess.run(cmd, stdout=log, stderr=subprocess.STDOUT, env=env, check=True)
    finally:
        for path in targets:
            if changed[path] != originals[path]:
                path.write_text(originals[path])


if __name__ == '__main__':
    main()
