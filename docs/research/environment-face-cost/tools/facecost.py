# Tool code of ter-yglw: the face cost of one timed run of tools/time.sh. The run draws in-turn faces in every n-th
# drawn frame and none in the others (TERTIUS_PROBE_FACE_EVERY=n). With n = 2 each face frame has two neighbours that
# are the same frame without a face. With n >= 4 the window is the face frame and the three frames after it (below). Per timed frame (frames.log, FrameTimeProbe.cs) and its faces (faces.log, FaceProbe.cs):
#   pair difference = the face frame's value minus the mean of its two neighbours' values, for
#     span     the frame's GPU span (first stamp to last), the measure that MoltenVK's late stamps leave valid
#     faces    the pass "environment faces" (the face's surfaces and its light)
#     prefilt  the pass "environment prefilter" (downsample, specular prefilter, irradiance of the drawn face)
#     both     faces + prefilter, a sum of neighbouring passes
#   face cost = the mean pair difference of the span over the face frames, with its standard error (sd / sqrt(n)).
# Pair differences are per face: divided by the faces of the face frame. It keeps only face frames that drew that count of
# faces, all of one map, and whose two neighbours drew none (a refresh of all faces, as
# after a cut, leaves out its frame and its neighbours). It splits the face frames by map, because map 0 (the root
# frame's map) and map 1 differ in what they draw, and gives the counts of each map's faces: kept instances, instances
# that only the size cull left out, draw commands and triangles (summed over the face's depth ranges).
# The run's load is the 1-minute load average of uptime at the start of the run (run.log), and its clock the mean GPU
# clock over the timed span (gpu-timings.json, gpuClock).
# Usage: python3 -I facecost.py <run dir> [<run dir> ...]   (prints one JSON object per run)
import json, math, re, sys
from pathlib import Path


def run(path):
    faces = {}
    for line in (path / 'faces.log').read_text().splitlines():
        parts = line.split()
        if parts:
            faces[int(parts[0])] = [tuple(int(v) for v in f.split(':')) for f in parts[1:]]
    frames = {}
    for line in (path / 'frames.log').read_text().splitlines():
        parts = line.split()
        passes = dict((k, float(v)) for k, v in (p.split('=') for p in parts[2:]))
        frames[int(parts[0])] = {'span': float(parts[1]), 'faces': passes.get('environment_faces', 0.0),
                                 'prefilt': passes.get('environment_prefilter', 0.0)}
        frames[int(parts[0])]['both'] = frames[int(parts[0])]['faces'] + frames[int(parts[0])]['prefilt']
    # The faces that a face frame draws: 1, or TERTIUS_PROBE_FACES_PER_FRAME, the most common count. Each pair difference
    # is divided by it, so the face cost is per face.
    counts = [len(v) for v in faces.values() if v]
    per = max(set(counts), key=counts.count)
    # The spacing n of the face frames (TERTIUS_PROBE_FACE_EVERY). With n = 2 the pair difference takes the two
    # neighbours. With n >= 4 it is (face frame + the frame after it) - (the two frames after those), because with two
    # frames in flight a face frame's work can spill into the start of the next frame (it did with 6 faces per frame:
    # the next frame's G-buffer pass grew from 0.16 to 0.8-1.4 ms), and the two frames after it are clean.
    marked = sorted(f for f, v in faces.items() if v)
    gaps = [b - a for a, b in zip(marked, marked[1:])]
    spacing = max(set(gaps), key=gaps.count) if gaps else 2
    diffs = {}
    for f, row in frames.items():
        drawn = faces.get(f, [])
        if len(drawn) != per or len({d[0] for d in drawn}) != 1:
            continue
        window = (f - 1, f + 1) if spacing < 4 else (f + 1, f + 2, f + 3)
        if any(g not in frames or faces.get(g) for g in window):
            continue
        m = drawn[0][0]
        if spacing < 4:
            d = {k: (row[k] - (frames[f - 1][k] + frames[f + 1][k]) / 2) / per for k in ('span', 'faces', 'prefilt', 'both')}
        else:
            d = {k: (row[k] + frames[f + 1][k] - frames[f + 2][k] - frames[f + 3][k]) / per
                 for k in ('span', 'faces', 'prefilt', 'both')}
        diffs.setdefault(m, []).append((d, drawn[0]))
    empty = [r for f, r in frames.items() if not faces.get(f)]
    log = (path / 'run.log').read_text()
    load = re.search(r'uptime-start:.*load averages?: ([\d.]+)', log)
    timings = json.loads((path / 'gpu-timings.json').read_text())
    clock = timings.get('gpuClock', {})
    out = {'run': path.name, 'faces_per_face_frame': per, 'spacing': spacing, 'load_1min': float(load.group(1)) if load else None,
           'clock_mhz': clock.get('meanMhz', clock.get('mhz')) if isinstance(clock, dict) else clock,
           'frames': len(frames), 'span_mean_ms': round(sum(r['span'] for r in frames.values()) / len(frames), 4),
           'empty_frame_passes_ms': {k: round(sum(r[k] for r in empty) / max(len(empty), 1), 4)
                                     for k in ('faces', 'prefilt')}}
    every = []
    for m, rows in sorted(diffs.items()):
        stats = {}
        for k in ('span', 'faces', 'prefilt', 'both'):
            v = [d[k] for d, _ in rows]
            mean = sum(v) / len(v)
            sd = math.sqrt(sum((x - mean) ** 2 for x in v) / max(len(v) - 1, 1))
            stats[k] = [round(mean, 4), round(sd / math.sqrt(len(v)), 4)]
        counts = [c for _, c in rows]
        stats['face_frames'] = len(rows)
        stats['kept_culled_commands_triangles_mean'] = [round(sum(c[i] for c in counts) / len(counts), 1)
                                                        for i in (4, 5, 2, 3)]
        out[f'map {m}'] = stats
        every += [d['span'] for d, _ in rows]
    mean = sum(every) / len(every)
    sd = math.sqrt(sum((x - mean) ** 2 for x in every) / max(len(every) - 1, 1))
    out['face_cost_span_ms'] = [round(mean, 4), round(sd / math.sqrt(len(every)), 4)]
    return out


for p in sys.argv[1:]:
    print(json.dumps(run(Path(p))))
