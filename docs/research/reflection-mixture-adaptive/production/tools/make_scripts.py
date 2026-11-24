#!/usr/bin/env python3
# Tool code of ter-fe63's stage 2. Writes the demo scripts of the production takes into <out>: the films of stage 1
# (tables/ of the stage 1 record), the two settled films, the mirror plate's still from the renderer survey's case,
# and the three timing views of stage 1's step 3.
# Usage: python3 -I make_scripts.py <repository root> <out>
import importlib.util, json, shutil, sys
from pathlib import Path

root, out = Path(sys.argv[1]), Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)
tables = root / 'docs/research/reflection-mixture-adaptive/tables'
for name in ('real-cut-nose', 'real-cut-lamps', 'pan-hall', 'walk-hall'):
    shutil.copy(tables / f'{name}.json', out / f'{name}.json')
window = {'width': 1920, 'height': 1080}
person = {'do': 'camera', 'entity': 'person'}
nose = {'do': 'run', 'ticks': 277003}
films = {
    'nose-film': [person, nose, {'do': 'film', 'name': 'n', 'frames': 360, 'every': 1}],
    'lamps-film': [person, {'do': 'film', 'name': 'n', 'frames': 360, 'every': 1}],
    'time-nose': [person, nose, {'do': 'film', 'name': 't', 'frames': 300, 'every': 1}],
    'time-lamps': [person, {'do': 'film', 'name': 't', 'frames': 300, 'every': 1}],
}
for name, steps in films.items():
    (out / f'{name}.json').write_text(json.dumps({'scene': 'orbit', 'window': window, 'steps': steps}))
(out / 'time-bay.json').write_text(json.dumps({
    'scene': 'demo-surface', 'window': window,
    'steps': [{'do': 'camera', 'entity': 'bay-camera'}, {'do': 'film', 'name': 't', 'frames': 300, 'every': 1}]}))
spec = importlib.util.spec_from_file_location('survey', root / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
cases = json.loads((root / 'tools/renderer-survey/cases.json').read_text())
plate = next(c for c in cases['stills'] if c['name'] == 'mirror-plate')
built, _ = survey.views_script(json.loads((root / 'scripts' / plate['script']).read_text()),
                               {plate['frame']: survey.captures('c', 1)})
(out / 'plate-still.json').write_text(json.dumps(built))
print('\n'.join(sorted(p.name for p in out.glob('*.json'))))
