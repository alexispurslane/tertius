import math, sys
exec(open('phase.py').read().split('print(\'far\'')[0])
rows = []
for line in open(sys.argv[1]):
    p = line.split()
    if len(p) >= 5 and p[0].isdigit() and p[1].isdigit():
        w, h, s, r, l = int(p[0]), int(p[1]), float(p[2]), float(p[3]), float(p[4])
        t = texel(w / h); q = 10 / t
        g = q - (math.floor(q - 0.5) + 1)
        rows.append(((w / h) / (16 / 9) - 1, g, r, l, w, h))
rows.sort()
# agreement: leak (right > 0.08) against the predicted gap
for lo, hi in ((-1, -0.25), (-0.25, 0.0), (0.0, 0.25), (0.25, 0.5)):
    sel = [r for r in rows if lo < r[1] <= hi]
    leak = sum(1 for r in sel if r[2] > 0.08)
    print(f'predicted gap {lo:+.2f}..{hi:+.2f} texel: {len(sel)} sizes, {leak} leak on the right, mean right {sum(r[2] for r in sel)/max(len(sel),1):.3f}')
for r in rows:
    if r[2] > 0.08 or r[1] > 0.3:
        print(f'{r[4]}x{r[5]} err {r[0]:+.3%} gap {r[1]:+.3f} right {r[2]:.3f} left {r[3]:.3f}')
