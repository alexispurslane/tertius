# Tool code (t1-gout). nose.py: the roof's front edge at z = -30 m at 16:9 over the first 20 s: the gap and the sill strip
# between the true shadow's start and the map's edge, per tick. Run from this folder (it reads phase.py).
import math
exec(open('phase.py').read().split("print('far'")[0])
n = math.sqrt(3.986004418e14 / (6.371e6 + 400e3) ** 3)
t16 = texel(16 / 9)
print('texel 16:9', t16)
# The roof's front edge (y = 4, z = -30) across light X = (0, -s, c); the roof covers u > u_edge.
# Texel borders at multiples of the texel (anchor = ship center); covered texel centers > u_edge.
# Sill receiver at (x, -2.85, -30 + e): inside the map footprint by 6.85 s + c e metres.
worst = []
for tick in range(0, 60 * 20, 6):
    t = tick / 60; v = n * t; s, c = math.sin(v), math.cos(v)
    q = (-4 * s - 30 * c) / t16
    first_covered = math.floor(q - 0.5) + 1          # first texel index whose center is > q
    map_edge = first_covered                          # border below that center, in texels
    gap = map_edge - q                                # map edge inside the roof's edge (positive = inside)
    strip = gap * t16 - 6.85 * s                      # metres of sill (at c ~ 1) between the true shadow's start and the map edge
    worst.append((tick, gap, strip))
leaky = [w for w in worst if w[2] > 0]
print('ticks with a leaking sill strip at the nose (tick, gap texel, strip m):', [(a, round(b, 3), round(c, 4)) for a, b, c in leaky[:40]])
print('last leaking tick:', leaky[-1][0] if leaky else None)
