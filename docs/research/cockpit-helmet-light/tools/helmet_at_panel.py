"""Diagnostic tool code: the helmet's light at the panel's real receiver samples.
usage: helmet_at_panel.py <vol normal> <vol black> <directions> <positions.hdr> <normals.hdr> [x0 y0 w h step]
Prints, per sampled pixel, the stored helmet diffuse light (normal minus black) read the way the composite reads it,
and the unoccluded form-factor bound from the helmet buckets' first-bounce exitant radiance."""
import sys, struct, math
sys.path.insert(0, sys.argv[0].rsplit('/', 1)[0])
from vol import Vol, C, L
a = Vol(sys.argv[1], int(sys.argv[3])); b = Vol(sys.argv[2], int(sys.argv[3]))
def hdr(path):
    d = open(path, 'rb').read(); n = len(d)//16
    return d, n
P, n = hdr(sys.argv[4]); N, _ = hdr(sys.argv[5])
W = 1280 if n == 1280*720 else int(round(math.sqrt(n*16/9))); H = n//W
x0, y0, w, h, st = (map(int, sys.argv[6:11]) if len(sys.argv) > 6 else (770, 65, 120, 130, 20))
sx, sy = W/1280, H/720
def smooth(e0, e1, x):
    t = min(max((x-e0)/(e1-e0), 0), 1); return t*t*(3-2*t)
def keep(r):
    pk = max(r); return 0.02 + 0.98*smooth(0.58, 0.78, (pk-min(r))/max(pk, 1e-12))
# helmet sources at level 0: buckets whose color differs, exitant = area x restrained(albedo x white)
src = []
for c in range(C):
    for s in range(6):
        an, ca, _ = a.bucket(0, c, s); _, cb, _ = b.bucket(0, c, s)
        if an[3] <= 0 or ca[:3] == cb[:3]: continue
        w_ = a.white(0, c, s)
        ra = [ca[i]/an[3]*w_[i] for i in range(3)]; rb = [cb[i]/an[3]*w_[i] for i in range(3)]
        ka, kb = keep(ra), keep(rb)
        flux = [(ra[i]*ka - rb[i]*kb) * an[3]/4096 * a.grids[0][3]**2 for i in range(3)]
        nn = [an[i]/an[3] for i in range(3)]; ln = math.sqrt(sum(v*v for v in nn)) or 1
        src.append((a.centre(0, c), [v/ln for v in nn], flux))
tot = [sum(s[2][i] for s in src) for i in range(3)]
print(f"render {W}x{H}; helmet first-bounce flux (m^2 x radiance) R {tot[0]:.5f} G {tot[1]:.5f} B {tot[2]:.5f} from {len(src)} buckets")
def stored(v, p, n):
    out = [0.0]*4; remaining = 1.0
    for l in range(L):
        g = v.grids[l]; q = [(p[i]-g[i])/g[3] for i in range(3)]
        edge = min(min(q), min(16-x for x in q)); wl = remaining*min(max(edge-0.5, 0), 1)
        if wl == 0: continue
        r = v.receive(l, p, n)
        for k in range(4): out[k] += wl*r[k]
        remaining -= wl
        if remaining <= 0: break
    return out
rows = []
for py in range(y0, y0+h, st):
    for px in range(x0, x0+w, st):
        i = int(py*sy)*W + int(px*sx)
        p = struct.unpack_from('<3f', P, 16*i); nn = struct.unpack_from('<3f', N, 16*i)
        sa, sb = stored(a, p, nn), stored(b, p, nn)
        e = [0.0]*3
        for q, ns, fl in src:
            d = [p[k]-q[k] for k in range(3)]; r2 = sum(v*v for v in d); r = math.sqrt(r2); u = [v/r for v in d]
            cs = max(0, sum(ns[k]*u[k] for k in range(3))); cr = max(0, -sum(nn[k]*u[k] for k in range(3)))
            for k in range(3): e[k] += fl[k]*cs*cr/r2/math.pi
        rows.append((px, py, p, nn, sa, sb, e))
for px, py, p, nn, sa, sb, e in rows:
    print(f"px({px},{py}) p({p[0]:+.2f},{p[1]:+.2f},{p[2]:+.2f}) n({nn[0]:+.2f},{nn[1]:+.2f},{nn[2]:+.2f}) total R {sa[0]:.5f} helmet R {sa[0]-sb[0]:+.6f} bound R {e[0]:.6f} ratio {(sa[0]-sb[0])/max(e[0],1e-12):.2f}")
m = lambda k: sum(r[k] for r in rows)/len(rows)
print(f"mean: total R {sum(r[4][0] for r in rows)/len(rows):.5f} helmet R {sum(r[4][0]-r[5][0] for r in rows)/len(rows):+.6f} bound R {sum(r[6][0] for r in rows)/len(rows):.6f}")
