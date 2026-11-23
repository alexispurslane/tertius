# Tool code of t1-g1jd. still.py <build> <view> <run>: one still of a frozen world, as the survey's still part draws it
# (tools/renderer-survey.py: STILL_CAPTURES captures of STILL_DRAWS draws each, the history continuing through them with
# TERTIUS_PROBE_STILL_CONTINUES=1, bloom off), with the binary /private/tmp/t0-5tyj/<build>/probe-bin (builds.sh), into
# /private/tmp/t0-5tyj/g1jd/runs/<build>-<view>-<run>/. The run dumps the slow diffuse history's length that one draw left
# (../scratch-history-dump.patch) into that folder's dump/. The views:
#   lamp:  the survey's still rect-lamps (scripts/rect-lamps.json, frame 240) at the render size 1072 x 603 of t1-g1jd
#          (1920 x 1080 at the fixed render scale 0.5584), the dump at draw 630;
#   cabin: the still cabin of the region take (t0-qvm1): the survey's switch case cabin (scene orbit, camera person, 60
#          ticks) with its lamps on, at the fixed render scale 2/3, the dump at draw 190;
#   bay:   the survey's still bay-sunbeam (scene demo-surface, camera bay-camera, 420 ticks) at the fixed render scale
#          2/3, the dump at draw 630.
# Draw n is the renderer's frame number n, counted from 0 at the run's first draw. The captures stay as v-s00 ... v-s27.
# Run it in the dev shell from the checkout's root: nix develop --command python3 -I <this file> g1-main lamp r1
import importlib.util
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('survey', ROOT / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)

VIEWS = {
    'lamp': ('stills', 'rect-lamps', '0.5584', 630),
    'cabin': ('switches', 'cabin', None, 190),
    'bay': ('stills', 'bay-sunbeam', None, 630),
}

build, view, run = sys.argv[1:4]
part, name, scale, draw = VIEWS[view]
case = next(c for c in survey.load_cases()[part] if c['name'] == name)
if part == 'switches':
    script, _ = survey.scene_script(case['scene'], case.get('camera'), case['ticks'], survey.captures('v', survey.STILL_CAPTURES)), True
else:
    script, _ = survey.still_script(*survey.source_of(case))
out = Path('/private/tmp/t0-5tyj/g1jd/runs') / f'{build}-{view}-{run}'
binary = Path('/private/tmp/t0-5tyj') / build / 'probe-bin' / 'Tertius.Demo'
env = {**survey.STILL_CONTINUES, 'TERTIUS_X_HISTORY_DUMP': f'{out}/dump:{draw}'}
extra = [*survey.NO_BLOOM, *(['--render-scale', scale] if scale else [])]
survey.render(out / 'frames', script, binary=binary, env=env, extra=extra)
dumps = sorted((out / 'dump').glob('*.f32'))
print(f'{out}: {len(dumps)} dump(s): {", ".join(d.name for d in dumps)}')
if not dumps:
    sys.exit(1)
