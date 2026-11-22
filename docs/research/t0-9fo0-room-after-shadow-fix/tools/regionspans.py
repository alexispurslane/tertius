# Tool code of t0-9fo0, part B. regionspans.py <log name> <still run> <map.png> <first,last>...: the region's drift test
# per span of draws, from one per-anchor log of scratch-region-log.patch (read by regionlog.py). <log name> is the log's
# path without its extension. Each span is a range of draw indices since the last reset, both ends included. Per span it
# prints:
#   - the anchor-draws at which the test ran (36 valid draws in a row), the fires among them and their share;
#   - the region's light change over the span: per anchor, the mean light of the span's last 18 draws over the mean of
#     its first 18, minus 1, as the median over the tested anchors of its magnitude, and the mean over the anchors. Two
#     means of 18 draws scatter with the light, so at an anchor whose light scatters widely this is not a trend measure
#     (regionnoise.py measures the trend);
#   - how the fires spread over the anchors: the share of all fires at the 10 anchors that fire most, and the share of
#     fires among the other anchors' tested anchor-draws;
#   - the anchors that fire most, with their share and their own light change over the span.
# A test with a known spread at 3 standard deviations fires at 0.27 % of its tests on noise. This test estimates its
# spread from 18 draws, so it fires at about 0.8 % on Gaussian noise (regionnoise.py). <map.png> marks, for the first
# span and the last, each anchor's share of fires on the still rect-lamps of <still run> (1920 x 1080 output from 1280 x
# 720 render): a square of 96 x 96 output pixels around the anchor's tap block, red with a strength from 0 to a share of
# 10 %, and the share in % as text. Run it through m.sh.
import sys
import numpy as np
import regionlog
from PIL import Image, ImageDraw

log, still, out = sys.argv[1], sys.argv[2], sys.argv[3]
spans = [tuple(int(v) for v in s.split(',')) for s in sys.argv[4:]]
index, light, tested, moved, wide = regionlog.load(log)
shares = {}
for first, last in spans:
    rows = (index >= first) & (index <= last)
    t, f = tested[rows], moved[rows] & tested[rows]
    share = f.sum() / max(t.sum(), 1)
    span_light = light[rows]
    change = span_light[-18:].mean(axis=0) / np.maximum(span_light[:18].mean(axis=0), 1e-9) - 1
    live = t.any(axis=0)
    per_anchor = f.sum(axis=0) / np.maximum(t.sum(axis=0), 1)
    shares[(first, last)] = per_anchor
    print(f'draws {first} to {last}: {rows.sum()} draws, anchor-draws tested {t.sum()}, fires {f.sum()}, share {100 * share:.2f} %,'
          f' light change over the span: median |change| {100 * np.median(np.abs(change[live])):.2f} %,'
          f' mean change {100 * change[live].mean():+.2f} %')
    busiest = np.argsort(f.sum(axis=0))[::-1][:10]
    rest = np.ones(per_anchor.shape, dtype=bool)
    rest[busiest] = False
    print(f'    the 10 anchors that fire most hold {100 * f[:, busiest].sum() / max(f.sum(), 1):.1f} % of the fires; the other '
          f'{rest.sum()} anchors fire at {100 * f[:, rest].sum() / max(t[:, rest].sum(), 1):.2f} % of their tested anchor-draws')
    for a in np.argsort(per_anchor)[::-1][:5]:
        if per_anchor[a] > 0:
            print(f'    anchor ({a % wide}, {a // wide}): share {100 * per_anchor[a]:.1f} %, its light change {100 * change[a]:+.2f} %')
base = Image.open(f's/{still}/images/still/rect-lamps.png').convert('RGB')
panels = []
for (first, last) in (spans[0], spans[-1]):
    image = base.copy()
    over = Image.new('RGBA', image.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(over)
    for a, share in enumerate(shares[(first, last)]):
        cx, cy = (a % wide) * 96 + 6, (a // wide) * 96 + 6
        strength = int(200 * min(share / 0.10, 1.0))
        d.rectangle([cx - 48, cy - 48, cx + 47, cy + 47], fill=(255, 0, 0, strength), outline=(0, 200, 0, 120))
        if share > 0:
            d.text((cx - 20, cy - 6), f'{100 * share:.1f}', fill=(255, 255, 255, 255))
    image = Image.alpha_composite(image.convert('RGBA'), over).convert('RGB').resize((960, 540), Image.LANCZOS)
    tile = Image.new('RGB', (960, 562), (0, 0, 0))
    tile.paste(image, (0, 22))
    ImageDraw.Draw(tile).text((4, 5), f'draws {first} to {last}: % of tested anchor-draws with a fire, per anchor', fill=(255, 255, 255))
    panels.append(tile)
sheet = Image.new('RGB', (960 * len(panels) + 6 * (len(panels) - 1), 562), (60, 60, 60))
for i, p in enumerate(panels):
    sheet.paste(p, (i * 966, 0))
sheet.save(out)
print(out, sheet.size)
