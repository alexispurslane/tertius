# Tool code of the region cap's takes (t0-qvm1). still.py <build> <view>: one still of a frozen world, as the survey's
# still part draws it (tools/renderer-survey.py: STILL_CAPTURES = 28 captures of STILL_DRAWS = 33 draws each, the history
# kept through them with TERTIUS_PROBE_STILL_CONTINUES=1, bloom off, the auto exposure), with the binary
# /private/tmp/t0-qvm1c/<build>/probe-bin (builds.sh), into /private/tmp/t0-qvm1c/runs/<build>-<view>/:
#   frames/  the captures v-s00 ... v-s27 (8-bit sRGB after the tone curve) and the demo's exposure.json;
#   dump/    the slow diffuse history's length that draws 131 (the survey's shown still, capture 3) and 890 (capture 26)
#            left (the replay's scratch-history-dump.patch);
#   region/  the region log of the whole run (the replay's scratch-region-log.patch). On the cap's build its b is the cut.
# The views, as the replay's still.py has them, and one more:
#   lamp:     the survey's still rect-lamps (scripts/rect-lamps.json, frame 240) at 1072 x 603 (the render scale 0.5584);
#   cabin:    the survey's switch case cabin (scene orbit, camera person, 60 ticks), lamps on, at the render scale 2/3;
#   bay:      the survey's still bay-sunbeam (scene demo-surface, camera bay-camera, 420 ticks) at 2/3;
#   cabinoff: the cabin with the switch's lamps off in every draw (the switch's control "off": TERTIUS_PROBE_LAMP_SEL of
#             the case and TERTIUS_PROBE_LAMP_OFF=0-100000000), at 2/3.
# Draw n is the renderer's frame number n, counted from 0 at the run's first draw. Capture k holds draw 33 k + 32. Run it
# in the dev shell from the checkout's root: nix develop --command python3 -I <this file> pm cabin
import importlib.util
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('survey', ROOT / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)

VIEWS = {
    'lamp': ('stills', 'rect-lamps', '0.5584'),
    'cabin': ('switches', 'cabin', None),
    'bay': ('stills', 'bay-sunbeam', None),
    'cabinoff': ('switches', 'cabin', None),
}
DUMPS = {view: '131,890' for view in VIEWS}

build, view = sys.argv[1:3]
part, name, scale = VIEWS[view]
case = next(c for c in survey.load_cases()[part] if c['name'] == name)
if part == 'switches':
    script = survey.scene_script(case['scene'], case.get('camera'), case['ticks'], survey.captures('v', survey.STILL_CAPTURES))
else:
    script, _ = survey.still_script(*survey.source_of(case))
out = Path('/private/tmp/t0-qvm1c/runs') / f'{build}-{view}'
binary = Path('/private/tmp/t0-qvm1c') / build / 'probe-bin' / 'Tertius.Demo'
env = {**survey.STILL_CONTINUES, 'TERTIUS_X_HISTORY_DUMP': f'{out}/dump:{DUMPS[view]}',
       'TERTIUS_X_REGION_LOG': f'{out}/region'}
if view == 'cabinoff':
    env.update(survey.switch_runs(case)['off'])
extra = [*survey.NO_BLOOM, *(['--render-scale', scale] if scale else [])]
survey.render(out / 'frames', script, binary=binary, env=env, extra=extra)
dumps = sorted((out / 'dump').glob('*.f32'))
logs = sorted((out / 'region').glob('region-*.txt'))
print(f'{out}: {len(dumps)} dump(s): {", ".join(d.name for d in dumps)}; region logs: {", ".join(l.name for l in logs)}')
if len(dumps) < 2 or not logs:
    sys.exit(1)
