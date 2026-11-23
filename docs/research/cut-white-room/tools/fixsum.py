# Tool code of t58-rcd6: the tables of the fix's timing and render scale takes (fix-run.sh).
#   timing: the GPU span (ms) of the cut's draw and the 10 draws after it, and of the 5 draws before it, per run, from
#     frames.txt (FrameTimeProbe: "<frame> <span ms> <pass>=<ms> ..."), with the load at the run's start; and the cut
#     draw's time in the passes 'environment faces' and 'environment prefilter'. cut-exterior.json's cut is draw 133:
#     the still's 33 draws (0 to 32), film a's 100 (33 to 132), then film b.
#   doorway: exit-walk.json, the exit demo's person, with the camera on the person, who walks out of the shuttle's cabin;
#     at about tick 1,548 the person's frame hands over from the shuttle to the station, which counts a cut. (The doorway
#     scene has one frame, and the orbit ship's airlock walk stays in the ship's frame, so neither hands anything over.)
#     The film's 120 draws start after 1,440 ticks without draws. The hand-off draw is the draw from 34 on whose
#     'environment faces' pass is the longest: it draws whole maps, where an ordinary draw draws one face. Its span, its
#     faces + prefilter time, and the mean span of the 5 draws before it.
#   scale: the render scale's requests (apply-scale.py's log) at the draws of the two cuts of cut-twice.json, draws 133
#     and 233: the catch-up request that each cut's draw asks for, which the catch-up draws after the cut before fitted.
# Usage: python3 -I fixsum.py timing|doorway|scale <run folder> ...
import re, sys
from pathlib import Path

mode, runs = sys.argv[1], [Path(p) for p in sys.argv[2:]]


def load(run):
    text = (run / 'load.txt').read_text().split()
    return text[-1] if text else '-'


if mode == 'timing':
    cut = 133
    print('| Run | Load | Draws 128 to 132 (ms) | Cut, draw 133 (ms) | Faces + prefilter in the cut draw (ms) | Draws 134 to 143 (ms) | Mean of 134 to 143 (ms) |')
    print('|---|---|---|---:|---:|---|---:|')
    for run in runs:
        spans, passes = {}, {}
        for line in (run / 'frames.txt').read_text().splitlines():
            parts = line.split()
            frame = int(parts[0])
            spans[frame] = float(parts[1])
            passes[frame] = {k: float(v) for k, v in (p.split('=') for p in parts[2:])}
        before = [spans.get(d) for d in range(cut - 5, cut)]
        after = [spans.get(d) for d in range(cut + 1, cut + 11)]
        p = passes.get(cut, {})
        faces = p.get('environment_faces', 0) + p.get('environment_prefilter', 0)
        fmt = lambda v: ', '.join('-' if x is None else f'{x:.2f}' for x in v)
        present = [x for x in after if x is not None]
        print(f'| {run.name} | {load(run)} | {fmt(before)} | {spans.get(cut, float("nan")):.2f} | {faces:.2f} | {fmt(after)} | '
              f'{sum(present) / max(len(present), 1):.2f} |')
elif mode == 'doorway':
    print('| Run | Load | Hand-off draw | Its span (ms) | Its faces + prefilter (ms) | Faces + prefilter of an ordinary draw, median (ms) | Mean span of the 5 draws before (ms) |')
    print('|---|---|---:|---:|---:|---:|---:|')
    for run in runs:
        spans, faces = {}, {}
        for line in (run / 'frames.txt').read_text().splitlines():
            parts = line.split()
            frame = int(parts[0])
            p = {k: float(v) for k, v in (q.split('=') for q in parts[2:])}
            spans[frame] = float(parts[1])
            faces[frame] = p.get('environment_faces', 0) + p.get('environment_prefilter', 0)
        film = [f for f in faces if f >= 34]
        handoff = max(film, key=lambda f: faces[f])
        ordinary = sorted(faces[f] for f in film)[len(film) // 2]
        before = [spans[f] for f in range(handoff - 5, handoff) if f in spans]
        print(f'| {run.name} | {load(run)} | {handoff} | {spans[handoff]:.2f} | {faces[handoff]:.2f} | {ordinary:.2f} | '
              f'{sum(before) / max(len(before), 1):.2f} |')
else:
    print('| Run | Load | Cut 1 (draw 133): catch-up request | Cut 2 (draw 233): catch-up request | Ordinary request at draw 232 |')
    print('|---|---|---:|---:|---:|')
    for run in runs:
        rows = {}
        for line in (run / 'scale.txt').read_text().splitlines():
            m = re.match(r'(\d+) cost=(\w+) catchup=([\d.]+) ordinary=([\d.]+) width=(\d+)', line)
            if m:
                rows[int(m[1])] = (m[2], float(m[3]), float(m[4]), int(m[5]))
        get = lambda d, i: f'{rows[d][i]:.4f}' if d in rows else '-'
        print(f'| {run.name} | {load(run)} | {get(133, 1)} | {get(233, 1)} | {get(232, 2)} |')
