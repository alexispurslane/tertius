# Tool code of t1-yi11's production check. takes.py <take> <build> [options]: renders one take with
# /private/tmp/t1-yi11-prod/<build>/probe-bin/Tertius.Demo (build.sh: the commit plus the survey's probe.patch and
# harness.py, no region rule changed), through tools/renderer-survey.py's render (1920 x 1080, the headless render scale
# 2/3, the shared GPU lock), into /private/tmp/t1-yi11-prod/takes/<take>-<build>[-<variant>]/. Every take writes the
# bounce light's ring log (region-ring.log). The takes:
#   bay [dif] [auto]: the frozen bay, bay-sunbeam (demo-surface's bay-camera after 420 ticks), a film of 1,660 draws of
#             a frozen world from the start (TERTIUS_X_FIRST_FILMED, TERTIUS_X_FROZEN: capture n is draw n), at -4.089
#             stops, or at the automatic exposure with "auto". "dif" draws the diffuse signal alone (TERTIUS_PROBE_SIGNAL=2).
#             It keeps the draws of BAY_KEEP.
#   walk switch|off: orbit's person camera after 60 ticks, the still (33 draws), 160 frames standing, 120 frames walking
#             (key W), 200 frames standing; the lamps z:-12:4 off from walk frame 60 (switch) or from draw 0 (off), at
#             4.08 stops. It keeps walk frames 50 to 119 every 5th and stand frames 0 to 159 every 4th.
#   turned:   rect-bay's film (the sunbeam leaves the bay, 180 frames) after 200 frozen draws of its first state, with the
#             camera turned 90 degrees left, so that the sun patch lies behind the camera, at -0.35925741 stops. It keeps
#             every 3rd film frame.
#   switch:   the survey's fair cabin switch: the three runs (switch, lamps always off, always on) with the survey's
#             switch options, and settle.json. It keeps no frame.
#   switchfilm: the switch run alone with bloom and the game's tone curve, keeping film frames 150, 165, 180, 200, 230,
#             260, 300, 360, 365, 380, 400 and 459.
#   cut a|b:  demo-exit: the person leaves the ship under camera a (cabin-camera) or b (the scene's own camera) for 399
#             ticks, then a camera cut to deck-camera and a film of 160 frames, at 1.0 stops. It keeps film frames 0 to 15,
#             then every 8th.
import importlib.util
import json
import sys
from pathlib import Path

ROOT = Path('/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144')
spec = importlib.util.spec_from_file_location('survey', ROOT / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
P = Path('/private/tmp/t1-yi11-prod')
BAY_KEEP = sorted({0, 1, 2, 4, 8, 16, 24, 36, 40, 48, 64, 96, 127, 152, 200, 300, *range(640, 660, 2), 659,
                   *range(660, 1611, 50), 1659})

take, build, *options = sys.argv[1:]
binary = P / build / 'probe-bin' / 'Tertius.Demo'
variant = '-'.join([take, build, *options])
out = P / 'takes' / variant
log = {'TERTIUS_X_REGION_LOG': str(out)}
if take == 'bay':
    steps = [{'do': 'camera', 'entity': 'bay-camera'}, {'do': 'run', 'ticks': 420}]
    steps += [{'do': 'capture', 'name': f'f-{n:06d}'} for n in range(1660)]
    env = {'TERTIUS_X_FIRST_FILMED': '1', 'TERTIUS_X_FROZEN': '1', **log,
           **survey.series_env(out, [f'f-{n:06d}' for n in BAY_KEEP])}
    if 'auto' not in options:
        env['TERTIUS_PROBE_EXPOSURE'] = '-4.089'
    if 'dif' in options:
        env['TERTIUS_PROBE_SIGNAL'] = '2'
    survey.render(out, {'scene': 'demo-surface', 'window': {'width': 1920, 'height': 1080}, 'steps': steps},
                  binary=binary, env=env)
elif take == 'walk':
    case = next(c for c in survey.load_cases()['walks'] if c['name'] == 'cabin-walk')
    script = survey.scene_script(case['scene'], case['camera'], case['ticks'], [
        {'do': 'capture', 'name': 'still'},
        {'do': 'film', 'name': 'f', 'frames': 160, 'every': 1},
        survey.walk_key(case, 'keyDown'),
        {'do': 'film', 'name': 'g', 'frames': 120, 'every': 1},
        survey.walk_key(case, 'keyUp'),
        {'do': 'film', 'name': 'h', 'frames': 200, 'every': 1},
    ])
    off = f'{survey.STILL_DRAWS + 160 + 60}-100000000' if options == ['switch'] else '0-100000000'
    keep = [survey.film_frame('g', k) for k in range(50, 120, 5)] + [survey.film_frame('h', k) for k in range(0, 160, 4)]
    survey.render(out, script, binary=binary,
                  env={'TERTIUS_PROBE_EXPOSURE': '4.08', 'TERTIUS_PROBE_LAMP_SEL': 'z:-12:4', 'TERTIUS_PROBE_LAMP_OFF': off,
                       **survey.series_env(out, keep), **log})
elif take == 'turned':
    case = next(c for c in survey.load_cases()['motion'] if c['name'] == 'rect-bay')
    script = survey.motion_case(case).film
    steps = script['steps']
    at = next(i for i, step in enumerate(steps) if step['do'] == 'run') + 1
    script = {**script, 'steps': steps[:at] + [{'do': 'capture', 'name': f'w-{n:06d}'} for n in range(200)] + steps[at:]}
    keep = [f'm{k:04d}-000000' for k in range(0, 180, 3)]
    survey.render(out, script, binary=binary,
                  env={'TERTIUS_PROBE_EXPOSURE': '-0.35925741', 'TERTIUS_X_FROZEN': '1', 'TERTIUS_X_FIRST_FILMED': '1',
                       'TERTIUS_X_YAW_DEGREES': '90', **survey.series_env(out, keep), **log})
elif take == 'switch':
    case = next(c for c in survey.load_cases()['switches'] if c['name'] == 'cabin')
    series = {}
    for label, env in survey.switch_runs(case).items():
        work = out / label
        survey.render(work, survey.switch_script(case), binary=binary,
                      env={'TERTIUS_PROBE_EXPOSURE': '4.08', **env, **survey.series_env(work, []),
                           'TERTIUS_X_REGION_LOG': str(work)}, extra=survey.PROBE_OPTIONS)
        series[label] = survey.probe_series(work, 'f')
    result = survey.switch_result(case, series)
    summary = {k: result[k] for k in ('regions', 'offDraws', 'onDraws', 'offWindow', 'onWindow', 'offDrawsWhole',
                                       'onDrawsWhole', 'change', 'failed')}
    (out / 'settle.json').write_text(json.dumps({**summary, 'series': series}))
    print(f'{out}: {json.dumps(summary)}')
elif take == 'switchfilm':
    case = next(c for c in survey.load_cases()['switches'] if c['name'] == 'cabin')
    env = survey.switch_runs(case)['switch']
    keep = [survey.film_frame('f', k) for k in (150, 165, 180, 200, 230, 260, 300, 360, 365, 380, 400, 459)]
    survey.render(out, survey.switch_script(case), binary=binary,
                  env={'TERTIUS_PROBE_EXPOSURE': '4.08', **env, **survey.series_env(out, keep), **log})
elif take == 'cut':
    first = [{'do': 'camera', 'entity': 'cabin-camera'}] if options == ['a'] else []
    steps = first + [{'do': 'post', 'to': {'kind': 'entity', 'id': 'person'}, 'event': 'LeaveShip', 'payload': None},
                     {'do': 'film', 'name': 'pre', 'frames': 399, 'every': 1},
                     {'do': 'camera', 'entity': 'deck-camera'},
                     {'do': 'film', 'name': 'post', 'frames': 160, 'every': 1}]
    keep = [f'post-{k:06d}' for k in [*range(16), *range(16, 160, 8)]] + ['pre-000398']
    survey.render(out, {'scene': 'demo-exit', 'window': {'width': 1920, 'height': 1080}, 'steps': steps},
                  binary=binary, env={'TERTIUS_PROBE_EXPOSURE': '1.0', **survey.series_env(out, keep), **log})
else:
    sys.exit(f'no take {take}')
print(f'{out}: {len(list(out.rglob("*.png")))} pictures')
