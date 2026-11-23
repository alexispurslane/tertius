# Tool code of ter-pdw8, group 4: one census dump in a draw of the survey's walk (case cabin-walk), for the share map of
# S2' in motion (sharemap.py). The script is the survey's walk_script cut after <frames> walk frames: the still of 33
# draws (draws 0 to 32), the stand film of 180 frames (draws 33 to 212), then the walk (draw 213 is its first frame).
# The render scale is fixed (the survey's walk runs at the dynamic scale), so that the census's per-pixel arrays keep
# one size. The variant comes from TERTIUS_PROBE_RAY_END. The PNG of the dumped draw's frame is kept.
# Usage: python3 -I walkmap.py <build copy root> <out dir> <dump draw> <scale> [frames]
import importlib.util, json, os, subprocess, sys
from pathlib import Path

root, out, dump, scale = Path(sys.argv[1]), Path(sys.argv[2]), int(sys.argv[3]), float(sys.argv[4])
frames = int(sys.argv[5]) if len(sys.argv) > 5 else 20
spec = importlib.util.spec_from_file_location('survey', root / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
case = dict(next(c for c in json.loads((root / 'tools' / 'renderer-survey' / 'cases.json').read_text())['walks']
                 if c['name'] == 'cabin-walk'))
case['walk'], case['after'] = frames, 1
out.mkdir(parents=True, exist_ok=True)
(out / 'script.json').write_text(json.dumps(survey.walk_script(case), indent=1))
first_walk = 33 + case['stand']
env = {**os.environ, 'TERTIUS_PROBE_EXPOSURE': os.environ.get('CENSUS_EXPOSURE', '4.08'),
       'TERTIUS_PROBE_SERIES': str(out / 'series.txt'), 'TERTIUS_PROBE_KEEP': f'g-{dump - first_walk:06d}',
       'TERTIUS_PROBE_CENSUS': str(out / 'census'), 'TERTIUS_PROBE_CENSUS_DRAWS': f'{dump - 3}-{dump + 1}',
       'TERTIUS_PROBE_CENSUS_DUMP_AT': str(dump)}
binary = root / 'src' / 'Tertius.Demo' / 'bin' / 'Release' / 'net11.0' / 'Tertius.Demo'
command = ['bash', str(root / 'tools' / 'gpu-lock.sh'), '--shared', str(out / 'lock.log'), str(binary), '--script',
           str(out / 'script.json'), '--headless', '--out', str(out), '--size', '1920x1080', '--render-scale', f'{scale}',
           *survey.PROBE_OPTIONS]
with open(out / 'run.log', 'w') as log:
    code = subprocess.run(command, env=env, cwd=root, stdout=log, stderr=subprocess.STDOUT).returncode
print('walk map', dump, 'exit', code)
sys.exit(code)
