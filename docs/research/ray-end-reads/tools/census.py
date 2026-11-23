# Tool code of ter-8pco (the ray census). Renders one census take: a still of one survey case
# (tools/renderer-survey/cases.json) at one fixed render scale, 20 continued captures of 33 draws (660 draws), with
# the census of RayCensus.cs over draws 240 to 639. It uses the survey's own script builders and
# TERTIUS_PROBE_STILL_CONTINUES=1, so the 20 captures draw one unchanged state with one history. The exposure is the
# renderer's own automatic exposure, as the game's: the caps act on pre-exposed light, so a fixed exposure would move
# them. RayCensus records the stops of each counted draw.
# ter-pdw8: CENSUS_DRAWS=<first>-<end> moves the counted draws (the second run of V0 counts 247 to 646), and the
# variant comes from TERTIUS_PROBE_RAY_END in the environment (apply-rayend.py).
# Usage: python3 -I census.py <build copy root> <case> <scale> <out dir> [<keep rects in output pixels "x0,y0,x1,y1;...">]
import importlib.util, json, os, subprocess, sys
from pathlib import Path

root, case_name, scale, out = Path(sys.argv[1]), sys.argv[2], float(sys.argv[3]), Path(sys.argv[4])
keeps_out = sys.argv[5] if len(sys.argv) > 5 else ''
spec = importlib.util.spec_from_file_location('survey', root / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
cases = json.loads((root / 'tools' / 'renderer-survey' / 'cases.json').read_text())
# sunlit-nose: the view of ter-fe63 (docs/research/back-face-depth-layer/sill-hits-tools/stills-sunlit-nose.json),
# the lamp hall of orbit with the sun through the nose window, as a still of an unchanged state.
cases['stills'].append({'name': 'sunlit-nose', 'scene': 'orbit', 'camera': 'person', 'ticks': 277243})
# rect-lamps-f62: the lamp hall's state at frame 62 of scripts/rect-lamps.json, in the speck films' bursts (t7-3e22).
cases['stills'].append({'name': 'rect-lamps-f62', 'script': 'rect-lamps.json', 'frame': 62})
case = next(c for c in cases['stills'] if c['name'] == case_name)
# With CENSUS_SIGNAL=<n>, the take is instead one still of the probe's light signal n alone (1 the direct light with the
# emission and the sky, 2 the bounce light), with no census: the record of what lights each region.
SIGNAL = os.environ.get('CENSUS_SIGNAL')
# CENSUS_CAPTURES=<n> renders n captures of 33 draws in place of 20, for a longer settling (ter-pdw8, test (b)).
CAPTURES = 1 if SIGNAL else int(os.environ.get('CENSUS_CAPTURES', '20'))
if 'script' in case:
    script = json.loads((root / 'scripts' / case['script']).read_text())
    built, first_is_still = survey.views_script(script, {case['frame']: survey.captures('c', CAPTURES)})
    first_is_still = first_is_still[case['frame']]
else:
    built = survey.scene_script(case['scene'], case.get('camera'), case['ticks'], survey.captures('c', CAPTURES))
    first_is_still = True
assert first_is_still, 'the census expects its first capture to be a still of 33 draws'
out.mkdir(parents=True, exist_ok=True)
(out / 'script.json').write_text(json.dumps(built, indent=1))
# The render size is the exact-ratio size of FixedRenderScale(scale) at 1920 x 1080: k steps of 16 x 9 px.
k = int(scale * 120 + 1e-6)
rw, rh = 16 * k, 9 * k
keeps = []
for rect in filter(None, keeps_out.split(';')):
    x0, y0, x1, y1 = map(int, rect.split(','))
    keeps.append(','.join(str(v) for v in (x0 * rw // 1920, y0 * rh // 1080, x1 * rw // 1920, y1 * rh // 1080)))
env = {**os.environ, 'TERTIUS_PROBE_STILL_CONTINUES': '1',
       'TERTIUS_PROBE_SERIES': str(out / 'series.txt'), 'TERTIUS_PROBE_KEEP': f'c-s{CAPTURES - 1:02d}',
       'TERTIUS_PROBE_CENSUS': str(out / 'census'), 'TERTIUS_PROBE_CENSUS_DRAWS': os.environ.get('CENSUS_DRAWS', '240-640'),
       'TERTIUS_PROBE_CENSUS_KEEP': ';'.join(keeps)}
# CENSUS_EXPOSURE=<stops> fixes the exposure, as the speck films' 4.08 stops; CENSUS_KEEP_ALL=1 keeps the PNG of every
# capture, for a speck count over the take.
if os.environ.get('CENSUS_EXPOSURE'):
    env['TERTIUS_PROBE_EXPOSURE'] = os.environ['CENSUS_EXPOSURE']
if os.environ.get('CENSUS_KEEP_ALL') == '1':
    env['TERTIUS_PROBE_KEEP'] = ','.join(f'c-s{i:02d}' for i in range(CAPTURES))
if SIGNAL:
    for key in ('TERTIUS_PROBE_CENSUS', 'TERTIUS_PROBE_CENSUS_DRAWS', 'TERTIUS_PROBE_CENSUS_KEEP'):
        env.pop(key)
    env['TERTIUS_PROBE_SIGNAL'] = SIGNAL
binary = root / 'src' / 'Tertius.Demo' / 'bin' / 'Release' / 'net11.0' / 'Tertius.Demo'
command = ['bash', str(root / 'tools' / 'gpu-lock.sh'), '--shared', str(out / 'lock.log'), str(binary), '--script',
           str(out / 'script.json'), '--headless', '--out', str(out), '--size', '1920x1080', '--render-scale',
           f'{scale}']
with open(out / 'run.log', 'w') as log:
    code = subprocess.run(command, env=env, cwd=root, stdout=log, stderr=subprocess.STDOUT).returncode
print(case_name, scale, f'{rw}x{rh}', 'exit', code)
sys.exit(code)
