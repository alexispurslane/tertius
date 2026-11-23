# Tool code of ter-pdw8, group 4: a film of the survey's motion part (tools/renderer-survey.py, motion_case) that keeps
# only the probe's per-frame region means (the whole image, then the 3 x 3 grid by rows, display luminance), with no
# PNG. With <stops> = "auto", it first finds the film's fixed exposure as the survey does (film_exposure: the mean of
# the automatic exposures of the stills of its middle and last frames) and prints it, so that the variant's film can
# run at V0's exposure. The variant comes from TERTIUS_PROBE_RAY_END. The result JSON holds "during" (one row per
# frame), as walkcmp.py reads it, and "stops".
# Usage: python3 -I motionfilm.py <build copy root> <out dir> <case name> <stops | auto>
import importlib.util, json, sys
from pathlib import Path

root, out, name, stops = Path(sys.argv[1]), Path(sys.argv[2]).resolve(), sys.argv[3], sys.argv[4]
spec = importlib.util.spec_from_file_location('survey', root / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
case = next(c for c in json.loads((root / 'tools' / 'renderer-survey' / 'cases.json').read_text())['motion']
            if c['name'] == name)
motion = survey.motion_case(case)
out.mkdir(parents=True, exist_ok=True)
binary = root / 'src' / 'Tertius.Demo' / 'bin' / 'Release' / 'net11.0' / 'Tertius.Demo'
(out / 'probe-bin').mkdir(exist_ok=True)
if not (out / 'probe-bin' / 'Tertius.Demo').exists():
    (out / 'probe-bin' / 'Tertius.Demo').symlink_to(binary)
if stops == 'auto':
    stops = f'{survey.film_exposure(out, name, motion):.4f}'
work = out / 'work'
survey.render(work, motion.film, binary=binary, env={'TERTIUS_PROBE_EXPOSURE': stops, **survey.series_env(work)},
              extra=survey.NO_BLOOM)
rows = survey.probe_rows(work)  # a script film names its frames m<kept>-<k>, a scene film m0000-<k>
during = [rows[n] for n in sorted(n for n in rows if n.startswith('m'))]
(out / f'{name}.json').write_text(json.dumps({'case': name, 'stops': float(stops), 'during': during,
                                             'regionDraws': [0] * len(during[0]), 'settleDraws': 0}))
survey.shutil.rmtree(work)
print(name, 'stops', stops, 'frames', len(during))
