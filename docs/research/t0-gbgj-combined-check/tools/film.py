# Tool code of t0-gbgj. film.py <out> <case> [stops]: the survey's own motion part (tools/renderer-survey.py, run_motion)
# on one case of cases.json's motion list, with two changes for the side-by-side video:
#   - The film's frames are kept: the survey deletes <out>/work/motion/<case>, and this script moves that folder to
#     <out>/frames instead.
#   - With [stops], the film and its stills render at that fixed exposure, so that two sides share one exposure. The
#     survey's own choice for the side (the mean auto exposure of the sampled frames' stills) is still computed, and
#     the result keeps it as "ownStops".
# The measures are the survey's. <out> holds probe-bin, as take.sh makes it. Run it in the dev shell from the root.
import importlib.util
import json
import shutil
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('survey', root / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
out, name = Path(sys.argv[1]).resolve(), sys.argv[2]
forced = float(sys.argv[3]) if len(sys.argv) > 3 else None
case = next(c for c in json.loads((root / 'tools' / 'renderer-survey' / 'cases.json').read_text())['motion']
            if c['name'] == name)
own = {}
survey_exposure = survey.film_exposure
def film_exposure(o, n, motion):
    own['stops'] = survey_exposure(o, n, motion)
    return own['stops'] if forced is None else forced
survey.film_exposure = film_exposure
film_work = out / 'work' / 'motion' / name
survey_rmtree = shutil.rmtree
def rmtree(path, *a, **k):
    if Path(path).resolve() == film_work:
        survey_rmtree(out / 'frames', ignore_errors=True)
        shutil.move(str(path), str(out / 'frames'))
    else:
        survey_rmtree(path, *a, **k)
survey.shutil.rmtree = rmtree
result = survey.run_motion(out, case, None)
saved = out / 'results' / 'motion' / f'{name}.json'
result = {**json.loads(saved.read_text()), 'ownStops': own['stops']}
saved.write_text(json.dumps(result, indent=1))
print({k: result[k] for k in ('frames', 'stops', 'ownStops', 'flicker', 'grainFlicker', 'blotch', 'level', 'failed')})
