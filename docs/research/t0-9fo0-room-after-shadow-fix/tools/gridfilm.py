# Tool code of t0-9fo0, part B. gridfilm.py <out>: the run "switch" of the survey's switch case "cabin" (the lamps go off
# at film frame 160 and on at 360), rendered as the survey renders it (renderer-survey.py: render, switch_script,
# switch_runs, the fixed exposure of 4.08 stops and the probe's options), but keeping the PNGs of film frames 159, 180,
# 220, 359, 380 and 420: the draw before each switch and about 20 and 60 draws after it. The survey's switch part keeps
# only 159, 168, 359 and 368. The PNGs go to <out>/images/grid/cabin-<frame>.png and the region means of every frame to
# <out>/series/grid/cabin-switch.json, as the survey keeps a run's series. <out> holds probe-bin, as take.sh makes it.
# Environment switches of the process, such as TERTIUS_X_REGION_LOG, reach the render. Run it in the dev shell from
# the checkout's root.
import importlib.util
import json
import shutil
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('survey', root / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
FRAMES = (159, 180, 220, 359, 380, 420)
out = Path(sys.argv[1]).resolve()
case = next(c for c in survey.load_cases()['switches'] if c['name'] == 'cabin')
work = out / 'work' / 'grid'
names = [survey.film_frame('f', k) for k in FRAMES]
env = {'TERTIUS_PROBE_EXPOSURE': '4.08', **survey.switch_runs(case)['switch'], **survey.series_env(work, names)}
survey.render(work, survey.switch_script(case), binary=survey.probe_binary(out), env=env, extra=survey.PROBE_OPTIONS)
series = survey.probe_series(work, 'f')
(out / 'series' / 'grid').mkdir(parents=True, exist_ok=True)
(out / 'series' / 'grid' / 'cabin-switch.json').write_text(json.dumps(series))
for k, name in zip(FRAMES, names):
    survey.keep_png(work / f'{name}.png', out / 'images' / 'grid' / f'cabin-{k:03d}.png')
shutil.rmtree(work)
print(out, len(series), 'frames')
