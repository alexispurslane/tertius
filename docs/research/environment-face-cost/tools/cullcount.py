# Tool code of ter-yglw: what the size cull leaves out, from the first line of each counts.sh run's faces.log (the
# still's first draw, which draws all six faces of every map). Per view, variant and map, summed over the map's six
# faces: the instances that the faces draw (kept), the instances that only the size cull left out (culled), the draw
# commands, and the triangles (each kept instance's mesh triangles times its depth ranges).
# Usage: python3 -I cullcount.py <counts root>
import sys
from pathlib import Path

print('| View | Face | Cull | Map | Faces | Kept instances | Culled instances | Draw commands | Triangles |')
print('|---|---:|---|---:|---:|---:|---:|---:|---:|')
for run in sorted(Path(sys.argv[1]).iterdir(), key=lambda p: (p.name.split('-')[0], -int(p.name.split('-')[1][1:]),
                                                                  p.name.split('-')[2])):
    log = run / 'faces.log'
    if not log.exists():
        continue
    first = log.read_text().splitlines()[0].split()[1:]
    maps = {}
    for f in first:
        m, face, commands, triangles, kept, culled = (int(v) for v in f.split(':'))
        s = maps.setdefault(m, [0, 0, 0, 0, 0])
        s[0] += 1; s[1] += kept; s[2] += culled; s[3] += commands; s[4] += triangles
    view, w, c = run.name.split('-')
    for m, s in sorted(maps.items()):
        print(f"| {view} | {w[1:]} | {'on' if c == 'c1' else 'off'} | {m} | {s[0]} | {s[1]} | {s[2]} | {s[3]} | {s[4]:,} |")
