# Tool code of the region cap's takes (t0-qvm1). film.py <build> <film> <stops>: the frames of a side-by-side video, one
# PNG per draw, at the fixed exposure <stops> (TERTIUS_PROBE_EXPOSURE), with the binary
# /private/tmp/t0-qvm1c/<build>/probe-bin (builds.sh), into /private/tmp/t0-qvm1c/films/<build>-<film>/. Bloom stays on,
# as the game draws it. The films:
#   still-<view>: a frozen still of the still.py view <view> (lamp, cabin, bay or cabinoff): the world runs to the view's
#                 state, and then each frame is a capture right after a capture, which TERTIUS_X_FROZEN=1 (t0-gumm's
#                 scratch-frozen.patch) draws once with no step of time, and TERTIUS_X_FIRST_FILMED=1 draws the first one
#                 once. So capture f-n is draw n. FROZEN_DRAWS captures, of which the PNGs from KEEP_FROM on are kept.
#   rect-bay:     the survey's motion film rect-bay (180 frames, one tick apart), m0000-n being draw n.
# Run it in the dev shell from the checkout's root.
import importlib.util
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('survey', ROOT / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)

FROZEN_DRAWS = int(os.environ.get('FROZEN_DRAWS', '660'))
KEEP_FROM = int(os.environ.get('KEEP_FROM', '420'))
VIEWS = {
    'lamp': ('stills', 'rect-lamps', '0.5584'),
    'cabin': ('switches', 'cabin', None),
    'bay': ('stills', 'bay-sunbeam', None),
    'cabinoff': ('switches', 'cabin', None),
}

build, film, stops = sys.argv[1:4]
out = Path('/private/tmp/t0-qvm1c/films') / f'{build}-{film}'
binary = Path('/private/tmp/t0-qvm1c') / build / 'probe-bin' / 'Tertius.Demo'
env = {'TERTIUS_PROBE_EXPOSURE': stops}
extra = []
if film.startswith('still-'):
    view = film[len('still-'):]
    part, name, scale = VIEWS[view]
    case = next(c for c in survey.load_cases()[part] if c['name'] == name)
    frames = [{'do': 'capture', 'name': f'f-{k:06d}'} for k in range(FROZEN_DRAWS)]
    if part == 'switches':
        script = survey.scene_script(case['scene'], case.get('camera'), case['ticks'], frames)
    else:
        source, frame = survey.source_of(case)
        script, _ = survey.views_script(source, {frame: frames})
    env.update({'TERTIUS_X_FROZEN': '1', 'TERTIUS_X_FIRST_FILMED': '1'})
    if view == 'cabinoff':
        env.update(survey.switch_runs(case)['off'])
    extra += ['--render-scale', scale] if scale else []
    keep = KEEP_FROM
else:
    case = next(c for c in survey.load_cases()['motion'] if c['name'] == film)
    script = survey.motion_case(case).film
    keep = 0
survey.render(out, script, binary=binary, env=env, extra=extra)
pngs = sorted(p for p in out.glob('*.png'))
for k, p in enumerate(pngs):
    if k < keep:
        p.unlink()
print(f'{out}: {len(pngs)} frames drawn, {len(pngs) - keep} kept from draw {keep}')
