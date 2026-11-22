import math
exec(open('phase.py').read().split("print('far'")[0])
ty = math.tan(math.pi / 6); tx = ty * 16 / 9; k2 = tx * tx + ty * ty
def blend_start(i):
    start = near if i == 0 else splits[i - 1]
    return splits[i] - 0.1 * (splits[i] - start)
for i in range(4):
    nr = near if i == 0 else blend_start(i - 1); fr = splits[i]
    c = min(0.5 * (fr + nr) * (1 + k2), fr); r = math.sqrt((fr - c) ** 2 + fr * fr * k2)
    print(f'cascade {i}: {nr:.2f} m to {fr:.2f} m, texel {2 * r / 2040:.5f} m at 16:9')
# Tiles: the symmetric filter, share = lit fraction. Map edge g texels inside the real edge (g in [0, 0.5]).
for g in (0.0, 0.25, 0.5):
    at_edge = lit_fraction(g)
    reach = max(d for d in [x / 100 for x in range(0, 400)] if lit_fraction(g - d) > 1e-9)
    print(f'tiles, map edge {g} texel inside the real edge: share at the real edge {at_edge:.3f}, light reaches {reach:.2f} texels inside the real edge')
