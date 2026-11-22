# Tool code of t0-gbgj. scalefilm.py <out>: the run "switch" of the survey's switch case "cabin" (the lamps go off at film
# frame 160 and on at 360), rendered as the survey renders it (renderer-survey.py: render, switch_script, switch_runs)
# at the fixed exposure of 4.08 stops, but with the default tone curve and bloom (no PROBE_OPTIONS), so that the kept
# frames are pictures, and keeping the PNGs of film frames 150, 180, 220, 355, 380 and 455: 10 draws before the lamps go
# off, 20, 60 and 195 draws after, and 20 and 95 draws after they come on. TERTIUS_X_RENDER_SCALE in the environment
# reaches the render (scratch-render-scale.patch). The PNGs go to <out>/images/scale/cabin-<frame>.png. <out> holds
# probe-bin, as take.sh makes it. Run it in the dev shell from the checkout's root. It is modeled on t0-9fo0's gridfilm.py.
import importlib.util
import shutil
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('survey', root / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
FRAMES = (150, 180, 220, 355, 380, 455)
out = Path(sys.argv[1]).resolve()
case = next(c for c in survey.load_cases()['switches'] if c['name'] == 'cabin')
work = out / 'work' / 'scale'
names = [survey.film_frame('f', k) for k in FRAMES]
env = {'TERTIUS_PROBE_EXPOSURE': '4.08', **survey.switch_runs(case)['switch'], **survey.series_env(work, names)}
survey.render(work, survey.switch_script(case), binary=survey.probe_binary(out), env=env)
for k, name in zip(FRAMES, names):
    survey.keep_png(work / f'{name}.png', out / 'images' / 'scale' / f'cabin-{k:03d}.png')
shutil.rmtree(work)
print(out, 'kept', len(FRAMES), 'frames')
