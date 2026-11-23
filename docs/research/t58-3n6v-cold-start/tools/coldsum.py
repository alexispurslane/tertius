"""Tool code of t58-3n6v: the cold start check's numbers. coldsum.py <job out/card folder>.
Draws: 0 start; then chase at 150, 301, 452 and the person camera at 151, 302, 453 (both are resets)."""
import sys, collections
root = sys.argv[1]
RESETS = [0, 150, 151, 301, 302, 452, 453]
CUTS = [151, 302, 453]  # the cut back to the person camera, the event of each cut
def kind(d):
    last = max(r for r in RESETS if r <= d)
    return ('C', d - last) if d - last < 8 else ('O', d - last)
def load(run):
    size = {}
    for l in open(f'{root}/{run}/size-none.txt'):
        f = l.split(); size[int(f[0])] = (int(f[1]), int(f[2]), float(f[3]))
    time = {}
    for l in open(f'{root}/{run}/time-none.txt'):
        f = l.split(); time[int(f[0])] = tuple(map(float, f[1:]))
    passes = {}
    for l in open(f'{root}/{run}/passes.txt'):
        f = l.rstrip('\n').split('\t'); d = int(f[0])
        if d in passes: continue
        passes[d] = (float(f[1]), {k: float(v) for k, v in (x.rsplit('=', 1) for x in f[2:])})
    return size, time, passes
runs = {r: load(r) for r in ('cold', 'warm')}
def row(run, d):
    size, time, passes = runs[run]
    w, h, s = size[d]; t = time.get(d, (float('nan'),) * 4)
    return f'{s:.3f} {w}x{h} {t[0]:6.1f}'
if __name__ == '__main__':
    for ev, at in [('start', 0)] + [(f'cut {i+1}', c) for i, c in enumerate(CUTS)]:
        print(f'== {ev} (draw {at})   draw kind,index | cold: request size span | warm: request size span')
        lo = at - 3 if at else 0
        for d in range(lo, at + 14):
            k, i = kind(d)
            print(f'{d:4d} {k}{i:<3d} | {row("cold", d)} | {row("warm", d)}')
    # back: draws until the request is within 0.02 of the warm run's, at each event
    print('== resets needed: catch-up request at each cut (first catch-up draw of the person camera)')
    for c in CUTS:
        print(c, 'cold', runs['cold'][0][c][2], 'warm', runs['warm'][0][c][2], 'chase', runs['cold'][0][c - 1][2], runs['warm'][0][c - 1][2])
