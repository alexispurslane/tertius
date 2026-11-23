# Tool code of ter-yglw: the face cost of one timed run of tools/time.sh. The run draws one in-turn face in every even
# drawn frame and none in the odd frames (TERTIUS_PROBE_FACE_EVERY=2), so each face frame has two neighbours that are
# the same frame without a face. Per timed frame (frames.log, FrameTimeProbe.cs) and its faces (faces.log, FaceProbe.cs):
#   pair difference = the face frame's value minus the mean of its two neighbours' values, for
#     span     the frame's GPU span (first stamp to last), the measure that MoltenVK's late stamps leave valid
#     faces    the pass "environment faces" (the face's surfaces and its light)
#     prefilt  the pass "environment prefilter" (downsample, specular prefilter, irradiance of the drawn face)
#     both     faces + prefilter, a sum of neighbouring passes
#   face cost = the mean pair difference of the span over the face frames, with its standard error (sd / sqrt(n)).
# It keeps only face frames that drew exactly one face and whose two neighbours drew none (a refresh of all faces, as
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
    diffs = {}
    for f, row in frames.items():
        drawn = faces.get(f, [])
        if len(drawn) != 1 or f - 1 not in frames or f + 1 not in frames:
            continue
        if faces.get(f - 1) or faces.get(f + 1):
            continue
        m = drawn[0][0]
        d = {k: row[k] - (frames[f - 1][k] + frames[f + 1][k]) / 2 for k in ('span', 'faces', 'prefilt', 'both')}
        diffs.setdefault(m, []).append((d, drawn[0]))
    empty = [r for f, r in frames.items() if not faces.get(f)]
    log = (path / 'run.log').read_text()
    load = re.search(r'uptime-start:.*load averages?: ([\d.]+)', log)
    timings = json.loads((path / 'gpu-timings.json').read_text())
    clock = timings.get('gpuClock', {})
    out = {'run': path.name, 'load_1min': float(load.group(1)) if load else None,
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
