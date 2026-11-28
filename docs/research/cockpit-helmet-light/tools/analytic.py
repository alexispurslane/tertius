"""Diagnostic tool code: the helmet's red light at panel cells, exact unoccluded form factor against the volume's lobes."""
import struct, sys, math
a = open(sys.argv[1], 'rb').read(); b = open(sys.argv[2], 'rb').read()
C, L, D, T, B = 4096, 3, 32, 64, 64
SURF = 64; WHITE = SURF + C*L*6*B; FIELD = WHITE + 16*L*T*6; BOUNCE = FIELD + 16*C*L*D; FILT = BOUNCE + 16*C*L*6
g = struct.unpack_from('<4f', a, 0)
src = []
for c in range(C):
    for s in range(6):
        k = c*6+s
        an = struct.unpack_from('<4i', a, SURF+B*k)
        if an[3] <= 0: continue
        ea = struct.unpack_from('<4f', a, SURF+B*k+48); eb = struct.unpack_from('<4f', b, SURF+B*k+48)
        dR = ea[0]-eb[0]
        if abs(dR) < 1e-9: continue
        n = [an[i]/an[3] for i in range(3)]; ln = math.sqrt(sum(x*x for x in n)); n = [x/ln for x in n]
        p = [g[i]+((c % 16, (c//16) % 16, c//256)[i]+.5)*g[3] for i in range(3)]
        src.append((p, n, dR/4096*g[3]**2))  # metres^2 x radiance
print("helmet red source, m^2 x radiance:", round(sum(s[2] for s in src), 5))
for (x, y, z) in [(7, 8, 4), (6, 8, 3), (6, 8, 2), (6, 8, 1)]:
    c = x+16*(y+16*z); p = [g[i]+((x, y, z)[i]+.5)*g[3] for i in range(3)]
    for side in (1, 3):
        nr = [0, 0, 0]; nr[side//2] = 1 if side % 2 == 0 else -1
        e = 0
        for q, ns, flux in src:
            d = [p[i]-q[i] for i in range(3)]; r2 = sum(v*v for v in d); r = math.sqrt(r2); u = [v/r for v in d]
            cs = max(0, sum(ns[i]*u[i] for i in range(3))); cr = max(0, -sum(nr[i]*u[i] for i in range(3)))
            e += flux*cs*cr/r2
        fa = struct.unpack_from('<4f', a, FILT+16*(c*12+side)); fb = struct.unpack_from('<4f', b, FILT+16*(c*12+side))
        print((x, y, z), 'side', side, f"exact lobe red {e/math.pi:.6f}  volume {fa[0]-fb[0]:.6f}  ratio {(fa[0]-fb[0])/(e/math.pi):.2f}")
