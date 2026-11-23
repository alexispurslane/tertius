# Tool code of t58-rcd6: an SVG chart of the cut log's means (means.py's lines) over the draws around the cut, one panel
# per run side by side, on one log axis. x is the draws since the cut: 0 is the draw that resets every history (the
# first frame of the film after the camera change). Each series is one mean of means.py; a value at or below 0 is left
# out, since a log axis has no place for it.
# Usage: python3 -I chart.py <out.svg> <before> <after> <title>=<means jsonl> ...
import json, math, sys

out, before, after = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
runs = []
for arg in sys.argv[4:]:
    title, path = arg.split('=', 1)
    rows = [json.loads(l) for l in open(path) if l.strip()]
    cut = next(i for i, r in enumerate(rows) if r.get('reset') == 1 and i > 1)
    runs.append((title, [(i - cut, r) for i, r in enumerate(rows) if -before <= i - cut <= after]))

SERIES = [('image', 'whole image (8-bit mean x 257)', '#111111'), ('history', 'diffuse history', '#d1495b'),
          ('hit', 'hit light (diffuse)', '#edae49'), ('closed', 'closed form (diffuse)', '#00798c'),
          ('irr0', 'irradiance, map 0', '#6a4c93'), ('irr1', 'irradiance, map 1', '#8ab17d'),
          ('irr2', 'irradiance, map 2', '#3d5a80')]
values = [r[k] for _, rows in runs for _, r in rows for k, _, _ in SERIES if isinstance(r.get(k), float) and r[k] > 0]
lo = 10 ** math.floor(math.log10(min(values)))
hi = 10 ** math.ceil(math.log10(max(values)))
W, H, L, T, B, G = 460, 360, 64, 40, 44, 24
width = L + len(runs) * (W + G) + 220


def y(v):
    return T + H * (1 - (math.log10(v) - math.log10(lo)) / (math.log10(hi) - math.log10(lo)))


svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{T + H + B + 10}" font-family="sans-serif" '
       f'font-size="12"><rect width="100%" height="100%" fill="white"/>']
for p, (title, rows) in enumerate(runs):
    x0 = L + p * (W + G)

    def x(d):
        return x0 + W * (d + before) / (before + after)

    svg.append(f'<text x="{x0 + W / 2}" y="{T - 14}" text-anchor="middle" font-weight="bold">{title}</text>')
    svg.append(f'<rect x="{x0}" y="{T}" width="{W}" height="{H}" fill="none" stroke="#999"/>')
    e = math.log10(lo)
    while e <= math.log10(hi) + 1e-9:
        svg.append(f'<line x1="{x0}" x2="{x0 + W}" y1="{y(10 ** e):.1f}" y2="{y(10 ** e):.1f}" stroke="#e5e5e5"/>')
        if p == 0:
            svg.append(f'<text x="{x0 - 6}" y="{y(10 ** e) + 4:.1f}" text-anchor="end">{10 ** e:g}</text>')
        e += 1
    for d in range(0, after + 1, 25):
        svg.append(f'<text x="{x(d):.1f}" y="{T + H + 16}" text-anchor="middle">{d}</text>')
    svg.append(f'<line x1="{x(0):.1f}" x2="{x(0):.1f}" y1="{T}" y2="{T + H}" stroke="#bbb" stroke-dasharray="3 3"/>')
    svg.append(f'<text x="{x0 + W / 2}" y="{T + H + 34}" text-anchor="middle">draws since the cut</text>')
    for key, _, color in SERIES:
        points = [(x(d), y(r[key])) for d, r in rows if isinstance(r.get(key), float) and r[key] > 0]
        if len(points) > 1:
            path = ' '.join(f'{px:.1f},{py:.1f}' for px, py in points)
            svg.append(f'<polyline points="{path}" fill="none" stroke="{color}" stroke-width="1.6"/>')
lx = L + len(runs) * (W + G)
for i, (key, label, color) in enumerate(SERIES):
    svg.append(f'<line x1="{lx}" x2="{lx + 18}" y1="{T + 10 + 18 * i}" y2="{T + 10 + 18 * i}" stroke="{color}" stroke-width="2"/>')
    svg.append(f'<text x="{lx + 24}" y="{T + 14 + 18 * i}">{label}</text>')
svg.append('</svg>')
open(out, 'w').write('\n'.join(svg))
