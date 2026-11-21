# ownshare.py <variant...>: the share of the render pixels that trace their own ray, per film frame, from the scratch
# probe's own-ray count (TERTIUS_X_OWN_COUNT, see take.sh). Each variant's folder runs/<variant>/own/ holds one file per
# process: <process id>.txt, whose first line is "# <command line>" and whose other lines are
# "<run draw> <reflection own rays> <diffuse own rays> <render width> <render height>". The counts come from the own-ray
# lists of the ray plan (ReadOwnRayCounts in VulkanRenderer.cs). The film process is the one whose command line names
# work/motion/rect-bay/script.json. Its last 180 lines are the film's 180 frames: film frame k is run draw 32 + k,
# as in t0-ion0's driftcount.py (draw 33 + k, counted from 1).
#
# Per variant it prints the mean share of each kind over film frames 0 to 59, 60 to 119 and 120 to 179, over the whole
# film, and at the middle (89) and last (179) film frame. With --csv <file> it also writes every film frame's shares.
import sys
from pathlib import Path

RUNS = Path('/private/tmp/t0-xdck/runs')
FILM_FRAMES = 180


def film_rows(variant):
    for f in (RUNS / variant / 'own').glob('*.txt'):
        lines = f.read_text().splitlines()
        if 'work/motion/rect-bay/script.json' in lines[0]:
            rows = [[int(x) for x in l.split()] for l in lines[1:]]
            rows = rows[-FILM_FRAMES:]
            assert len(rows) == FILM_FRAMES, (variant, len(rows))
            return [(r[1] / (r[3] * r[4]), r[2] / (r[3] * r[4])) for r in rows]
    raise SystemExit(f'{variant}: no film process in {RUNS / variant / "own"}')


def mean(xs):
    return sum(xs) / len(xs)


args = sys.argv[1:]
csv = None
if '--csv' in args:
    i = args.index('--csv')
    csv = args[i + 1]
    del args[i:i + 2]
table = {}
print(f'{"variant":10} {"kind":10} {"0-59":>6} {"60-119":>6} {"120-179":>7} {"film":>6} {"f89":>6} {"f179":>6}')
for v in args:
    shares = film_rows(v)
    table[v] = shares
    for k, kind in enumerate(('reflection', 'diffuse')):
        s = [x[k] for x in shares]
        print(f'{v:10} {kind:10} {mean(s[0:60]):6.3f} {mean(s[60:120]):6.3f} {mean(s[120:180]):7.3f} '
              f'{mean(s):6.3f} {s[89]:6.3f} {s[179]:6.3f}')
if csv:
    with open(csv, 'w') as out:
        out.write('frame,' + ','.join(f'{v}_reflection,{v}_diffuse' for v in table) + '\n')
        for i in range(FILM_FRAMES):
            out.write(f'{i},' + ','.join(f'{table[v][i][0]:.4f},{table[v][i][1]:.4f}' for v in table) + '\n')
