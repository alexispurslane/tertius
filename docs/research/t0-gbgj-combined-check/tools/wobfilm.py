# Tool code of t0-gbgj. wobfilm.py <out>: the switch "cabin" with the fair lamp switch (the probe's default, glow off with
# the light), rendered as the acceptance lead's macdyn.sh pictures renders it: 1920 x 1080, 4.08 stops, the default tone
# curve and bloom, the render scale of each draw played back from TERTIUS_X_SIZE_REPLAY (set by the caller), and the
# run's own size log in <out>. It keeps the PNGs of film frames 140 to 459 (20 draws before the lamps go off to 100
# draws after they come on; film frame k is draw 33 + k) as <out>/frames/f-<k>.png. Run it in the dev shell from the
# checkout's root, with <out>/probe-bin in place.
import importlib.util
import os
import shutil
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('survey', root / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
out = Path(sys.argv[1]).resolve()
case = next(c for c in survey.load_cases()['switches'] if c['name'] == 'cabin')
work = out / 'work' / 'film'
names = [survey.film_frame('f', k) for k in range(140, 460)]
env = {'TERTIUS_PROBE_EXPOSURE': '4.08', 'TERTIUS_X_FRAME_TIME_TARGET': '16.6667', 'TERTIUS_X_SIZE_LOG': str(out),
       **survey.switch_runs(case)['switch'], **survey.series_env(work, names)}
print('lamps', env['TERTIUS_PROBE_LAMP_SEL'], env['TERTIUS_PROBE_LAMP_OFF'], 'replay', os.environ.get('TERTIUS_X_SIZE_REPLAY'))
survey.render(work, survey.switch_script(case), binary=survey.probe_binary(out), env=env)
(out / 'frames').mkdir(exist_ok=True)
for k, name in zip(range(140, 460), names):
    survey.keep_png(work / f'{name}.png', out / 'frames' / f'f-{k:06d}.png')
shutil.rmtree(work)
print(out, 'kept', len(names), 'frames')
