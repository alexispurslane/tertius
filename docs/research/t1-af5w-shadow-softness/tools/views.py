"""Tool code of t1-af5w. views.py <out folder>: writes one still script per view of the shadow measure into <out folder>.
Each script is the view's script cut at the view's frame, as tools/renderer-survey/cases.json counts frames (each
capture and each film frame counts one). A film up to the frame becomes a run of as many ticks as the film's frames
take (frame k of a film of `every` ticks per frame: k * every ticks), and dumps go. Then three captures a, b and c follow:
a still draws a frozen world, and the step of the exposure from b to c shows whether it settled."""
import json, os, sys
R = os.path.abspath(os.path.join(os.path.dirname(__file__), '../../../..'))
VIEWS = [  # name, script, frame; or name, scene, camera, ticks
    ('cabin-light-cabin', 'cabin-light.json', 0),
    ('surface-cabin', 'demo-surface.json', 4),
    ('bay-sunbeam', 'demo-surface', 'bay-camera', 420),
    ('lamp-hall-16', 'lamp-hall-16.json', 0),
    ('rect-lamps', 'rect-lamps.json', 240),
    ('exit-cabin', 'demo-exit.json', 200),
    ('exit-deck', 'demo-exit.json', 500),
    ('surface-apron', 'demo-surface.json', 2),
    ('surface-overview', 'demo-surface.json', 6),
    ('launch-pad', 'demo-launch.json', 2),
    ('earth-pad', 'earth-system.json', 4),
    ('mars-day', 'mars-air.json', 0),
]
STILLS = [{'do': 'capture', 'name': n} for n in 'abc']


def cut(steps, frame):
    out, k = [], 0
    for st in steps:
        n = 1 if st['do'] == 'capture' else st.get('frames', 0) if st['do'] == 'film' else 0
        if frame < k + n:
            if st['do'] == 'film' and frame > k:
                out.append({'do': 'run', 'ticks': (frame - k) * st.get('every', 1)})
            return out + STILLS
        k += n
        if st['do'] == 'film':
            out.append({'do': 'run', 'ticks': n * st.get('every', 1)})
        elif st['do'] not in ('capture', 'dump'):
            out.append(st)
    raise ValueError(frame)


out = sys.argv[1]
os.makedirs(out, exist_ok=True)
for v in VIEWS:
    if len(v) == 3:
        name, script, frame = v
        d = json.load(open(f'{R}/scripts/{script}'))
        d['steps'] = cut(d['steps'], frame)
    else:
        name, scene, camera, ticks = v
        d = {'scene': scene, 'steps': [{'do': 'camera', 'entity': camera}, {'do': 'run', 'ticks': ticks}] + STILLS}
    d['window'] = {'width': 1920, 'height': 1080}
    json.dump(d, open(f'{out}/{name}.json', 'w'), indent=1)
    print(name, [s['do'] + ':' + str(s.get('ticks', s.get('entity', s.get('name', '')))) for s in d['steps']])
