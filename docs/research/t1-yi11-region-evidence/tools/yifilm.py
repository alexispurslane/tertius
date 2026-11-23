# Tool code of t1-yi11. yifilm.py <build> <name> <film> [VAR=value...]: the frames of a light change, one PNG per draw,
# with the probe build /private/tmp/t1-yi11/<build>/probe-bin (builds.sh), through tools/renderer-survey.py's scripts and
# render (1920 x 1080, the headless render scale 2/3, the shared GPU lock), into
# /private/tmp/t1-yi11/films/<name>/. The extra VAR=value words go into the environment (the column's knobs).
#   rect-bay: the survey's motion film rect-bay (180 frames one tick apart, the sunbeam leaves the bay) at the fixed
#             exposure of main's film in t0-qvm1's takes, -0.35925741 stops, bloom on as the game draws it. It keeps every
#             frame: m0000-n is draw n.
#   switch:   the survey's switch case cabin (orbit's person camera after 60 ticks, the lamps z:-12:4 off in film frames
#             160 to 360) at 4.08 stops. Three runs, as the survey's switch part: the switch (it keeps film frames 150 to
#             459 under switch/), and the controls with the lamps always off and always on, with the survey's switch
#             options (survey.PROBE_OPTIONS: bloom off, the Hable tone curve). It writes settle.json, the
#             survey's switch_result: the settle in draws of the slowest changed region and of the whole image, after off
#             and after on.
#   switch-film: the switch run alone, without the survey's switch options (bloom on, the game's tone curve), for the eye.
#             With those options the probe's kept PNGs come out as a false-colour pattern (seen on 2026-11-23), while
#             its measures match t0-qvm1. It keeps film frames 150 to 459.
# Run it in the dev shell from the checkout's root.
import importlib.util
import json
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('survey', ROOT / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)

build, name, film, *pairs = sys.argv[1:]
knobs = dict(p.split('=', 1) for p in pairs)
out = Path('/private/tmp/t1-yi11/films') / name
binary = Path('/private/tmp/t1-yi11') / build / 'probe-bin' / 'Tertius.Demo'
shutil.rmtree(out, ignore_errors=True)
if film == 'rect-bay':
    case = next(c for c in survey.load_cases()['motion'] if c['name'] == 'rect-bay')
    survey.render(out, survey.motion_case(case).film, binary=binary,
                  env={'TERTIUS_PROBE_EXPOSURE': '-0.35925741', **knobs})
    print(f'{out}: {len(list(out.glob("*.png")))} frames')
elif film == 'switch':
    case = next(c for c in survey.load_cases()['switches'] if c['name'] == 'cabin')
    series = {}
    for label, env in survey.switch_runs(case).items():
        work = out / label
        keep = [survey.film_frame('f', k) for k in range(150, 460)] if label == 'switch' else []
        survey.render(work, survey.switch_script(case), binary=binary,
                      env={'TERTIUS_PROBE_EXPOSURE': '4.08', **env, **survey.series_env(work, keep), **knobs},
                      extra=survey.PROBE_OPTIONS)
        series[label] = survey.probe_series(work, 'f')
    result = survey.switch_result(case, series)
    summary = {k: result[k] for k in ('regions', 'offDraws', 'onDraws', 'offWindow', 'onWindow', 'offDrawsWhole',
                                       'onDrawsWhole', 'change', 'failed')}
    (out / 'settle.json').write_text(json.dumps({**summary, 'series': series}))
    print(f'{out}: {json.dumps(summary)}')
elif film == 'switch-film':
    case = next(c for c in survey.load_cases()['switches'] if c['name'] == 'cabin')
    env = survey.switch_runs(case)['switch']
    keep = [survey.film_frame('f', k) for k in range(150, 460)]
    survey.render(out, survey.switch_script(case), binary=binary,
                  env={'TERTIUS_PROBE_EXPOSURE': '4.08', **env, **survey.series_env(out, keep), **knobs})
    print(f'{out}: {len(list(out.glob("*.png")))} frames')
else:
    sys.exit(f'no film {film}')
