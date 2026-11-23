# Tool code of the region repair's replay (t0-qvm1). still.py <build> <view> <tag> [signal]: one still of a frozen world, as
# the survey's still part draws it (tools/renderer-survey.py: STILL_CAPTURES = 28 captures of STILL_DRAWS = 33 draws each,
# the history kept through them with TERTIUS_PROBE_STILL_CONTINUES=1, bloom off), with the binary
# /private/tmp/t0-5tyj/<build>/probe-bin (builds.sh), into /private/tmp/t0-5tyj/r2/runs/<build>-<view>-<tag>/:
#   frames/  the captures v-s00 ... v-s27 (8-bit sRGB after the tone curve; with a signal, see below);
#   dump/    the slow diffuse history's length that the draws DUMPS left (../scratch-history-dump.patch);
#   region/  the region log of the whole run (../scratch-region-log.patch): region-none.txt and region-none.f32.
# With signal 2 the composite shows the bounce light alone (TERTIUS_PROBE_SIGNAL=2 of the probe patch), at the fixed
# exposure of 0 stops (TERTIUS_PROBE_EXPOSURE=0), and the captures hold the probe's 24-bit log2 luminance of the HDR image
# after FSR and before the tone curve (--tone-curve hable; tools/renderer-survey.py, probe_luminance). The views:
#   lamp:  the survey's still rect-lamps (scripts/rect-lamps.json, frame 240) at 1072 x 603 (the fixed render scale 0.5584);
#   cabin: the still cabin of the region take: the survey's switch case cabin (scene orbit, camera person, 60 ticks),
#          lamps on, at the fixed render scale 2/3;
#   bay:   the survey's still bay-sunbeam (scene demo-surface, camera bay-camera, 420 ticks) at 2/3;
#   exit:  the survey's still exit-cabin (scripts/demo-exit.json, frame 200) at 2/3.
# Draw n is the renderer's frame number n, counted from 0 at the run's first draw. Run it in the dev shell from the
# checkout's root: nix develop --command python3 -I <this file> r2-main cabin full
import importlib.util
import os
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
    'exit': ('stills', 'exit-cabin', None),
}
DUMPS = {'lamp': '630,890', 'cabin': '190,890', 'bay': '630,890', 'exit': '630,890'}

build, view, tag = sys.argv[1:4]
signal = sys.argv[4] if len(sys.argv) > 4 else None
part, name, scale = VIEWS[view]
case = next(c for c in survey.load_cases()[part] if c['name'] == name)
if part == 'switches':
    script = survey.scene_script(case['scene'], case.get('camera'), case['ticks'], survey.captures('v', survey.STILL_CAPTURES))
else:
    script, _ = survey.still_script(*survey.source_of(case))
out = Path('/private/tmp/t0-5tyj/r2/runs') / f'{build}-{view}-{tag}'
binary = Path('/private/tmp/t0-5tyj') / build / 'probe-bin' / 'Tertius.Demo'
env = {**survey.STILL_CONTINUES, 'TERTIUS_X_HISTORY_DUMP': f'{out}/dump:{DUMPS[view]}',
       'TERTIUS_X_REGION_LOG': f'{out}/region',
       **({'TERTIUS_PROBE_SIGNAL': signal, 'TERTIUS_PROBE_EXPOSURE': '0'} if signal else {})}
extra = [*survey.NO_BLOOM, *(['--render-scale', scale] if scale else []), *(['--tone-curve', 'hable'] if signal else [])]
survey.render(out / 'frames', script, binary=binary, env=env, extra=extra)
dumps = sorted((out / 'dump').glob('*.f32'))
logs = sorted((out / 'region').glob('region-*.txt'))
print(f'{out}: {len(dumps)} dump(s): {", ".join(d.name for d in dumps)}; region logs: {", ".join(l.name for l in logs)}')
if len(dumps) < 2 or not logs:
    sys.exit(1)
