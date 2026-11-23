# Tool code of the region cap's takes (t0-qvm1). timing.py <rounds> [first round]: the GPU span of the bounce light's
# accumulate pass ("accumulate indirect diffuse": the temporal and the spatial dispatch of the bounce light; the timing has
# no stamp between the two, and only the spatial dispatch changed), main's code (tm) against the cap (tc), the plain
# Release demos of builds.sh. Each round times both builds in turn (tm first in even rounds, tc first in odd ones) on the
# survey's timing film of two views (TIMING_FRAMES = 120 frames one tick apart, timed as the survey's timing part does:
# two frames in flight, no image, beside gpu-clock): the still bay-sunbeam and the cabin of the switch case. A round
# starts when the one-minute load is under 8. Rounds number from [first round] on, so that a later take keeps the
# earlier rounds' folders. It writes /private/tmp/t0-qvm1c/timing/<round>-<build>-<view>/ and prints one line per run:
# the pass's mean and median span, the frame's median span, in ms, and the load before the run. TIMING_BUILDS names
# the two builds (default "tm tc"), and TIMING_ENV adds variables to each run, as NAME=value pairs split by commas: so
# "pm pc" with TERTIUS_X_EVIDENCE_ACTION=0 times the probe builds with the evidence's action dropped on both sides.
import importlib.util
import json
import os
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('survey', ROOT / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)

OUT = Path('/private/tmp/t0-qvm1c/timing')
PASS = 'accumulate indirect diffuse'


def script(view):
    if view == 'bay':
        case = next(c for c in survey.load_cases()['stills'] if c['name'] == 'bay-sunbeam')
        return survey.timing_script(*survey.source_of(case))
    case = next(c for c in survey.load_cases()['switches'] if c['name'] == 'cabin')
    return survey.scene_script(case['scene'], case['camera'], case['ticks'],
                               [{'do': 'film', 'name': 't', 'frames': survey.TIMING_FRAMES, 'every': 1}])


def load():
    return float(subprocess.run(['sysctl', '-n', 'vm.loadavg'], capture_output=True, text=True).stdout.split()[1])


EXTRA = dict(kv.split('=', 1) for kv in os.environ.get('TIMING_ENV', '').split(',') if kv)
first = int(sys.argv[2]) if len(sys.argv) > 2 else 0
for k in range(first, first + int(sys.argv[1])):
    while load() >= 8:
        time.sleep(30)
    builds = os.environ.get('TIMING_BUILDS', 'tm tc').split()
    order = builds if k % 2 == 0 else builds[::-1]
    for view in ('bay', 'cabin'):
        for build in order:
            out = OUT / f'{k}-{build}-{view}'
            before = load()
            survey.render(out, script(view), binary=Path('/private/tmp/t0-qvm1c') / build / 'probe-bin' / 'Tertius.Demo',
                          env=EXTRA, timed=True)
            t = json.loads((out / 'gpu-timings.json').read_text())
            p = next(p for p in t['passes'] if p['pass'] == PASS)
            print(f"round {k} {build} {view}: pass mean {p['meanMs']:.4f} median {p['medianMs']:.4f}, frame median "
                  f"{t['frame']['medianMs']:.3f} ms, load {before:.1f}", flush=True)
