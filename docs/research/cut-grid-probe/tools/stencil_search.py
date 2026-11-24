# Tool code of t1-3er4: searches integer stencils of 4 pairs +-d whose second moments equal the 3 x 3 square's exactly
# (sum dx^2 = sum dy^2 = 3 s^2, sum dx dy = 0), with unique taps. It finds 3 at spacing 2 and 3 at spacing 4 (two of them
# mirror images), too few for a palette. So the probe rounds rotated squares instead. Young histories trace their own
# rays (screen_trace_plan.comp, line 224), so taps in one 2 x 2 block do not share a ray.
import itertools, math, sys
def sets(s, minsep):
    rmax = 2 * s
    reps = [(x, y) for x in range(-rmax, rmax + 1) for y in range(0, rmax + 1)
            if (y > 0 or x > 0) and x * x + y * y <= (1.6 * s) ** 2]
    out = []
    for combo in itertools.combinations(reps, 4):
        if sum(x * x for x, _ in combo) != 3 * s * s or sum(y * y for _, y in combo) != 3 * s * s: continue
        if sum(x * y for x, y in combo) != 0: continue
        taps = [(0, 0)] + [p for x, y in combo for p in ((x, y), (-x, -y))]
        sep = min(math.dist(a, b) for a, b in itertools.combinations(taps, 2))
        if sep < minsep: continue
        w = [x * x + y * y for x, y in combo]
        c4 = sum(wi * math.cos(4 * math.atan2(y, x)) for wi, (x, y) in zip(w, combo)) / sum(w)
        s4 = sum(wi * math.sin(4 * math.atan2(y, x)) for wi, (x, y) in zip(w, combo)) / sum(w)
        out.append((combo, round(sep, 2), round(math.hypot(c4, s4), 2), round(math.degrees(math.atan2(s4, c4)) / 4, 1),
                    sorted(round(math.hypot(x, y), 2) for x, y in combo)))
    return out
for s, minsep in ((2, 1.0), (4, 2.0)):
    r = sets(s, minsep)
    print(f"s={s}: {len(r)} sets")
    for row in sorted(r, key=lambda t: (-t[1], t[3])): print("  ", *row)
