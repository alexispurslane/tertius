# Tool code of ter-pdw8, question 2 (the flecks at the foot of the far wall): for one census take of rect-lamps-f62 with
# keep rectangle <k> on the floor at the foot of the far wall, the brightest samples of each signal and where they hit.
# Inputs (apply-rayend2.py): census.keep<k> (per kept sample: f4 X, f4 Xpre, u1 code (bin + 64 guided), f4 reflection
# sample luminance, f4 reflection hit distance) and census.hits<k> (per kept sample: f4 diffuse hit x, y, level, then the
# same for the reflection sample's ray; -1 for none), in render pixels, row by row and draw by draw.
#   bright   = the samples above <factor> times the signal's mean over all kept samples (default 10)
#   per signal: the count, their share of the signal's light, the guided share, the hit distance (diffuse: from the bin),
#               and the hit rows that hold most of their light, with the share of light per row
#   sill row = render rows <row0>..<row1> (default 312..316 at 1072 x 603, the window's lower edge of ter-8pco's dump)
# Usage: python3 sill.py <take dir> <k> [factor] [row0 row1]
import json, sys
from pathlib import Path
import numpy as np

take, k = Path(sys.argv[1]), int(sys.argv[2])
factor = float(sys.argv[3]) if len(sys.argv) > 3 else 10.0
row0, row1 = (int(sys.argv[4]), int(sys.argv[5])) if len(sys.argv) > 5 else (312, 316)
meta = json.loads((take / 'census.json').read_text())
keep = np.fromfile(take / f'census.keep{k}', dtype=np.dtype([('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'),
                                                               ('xs', '<f4'), ('ds', '<f4')]))
hits = np.fromfile(take / f'census.hits{k}', '<f4').reshape(-1, 6)
assert len(hits) == len(keep), (len(hits), len(keep))
out = {'samples': int(len(keep)), 'keep_rect_render_px': meta['Keeps'][k]}
for name, value, hx, hy, guided, dist in (
        ('diffuse', keep['x'], hits[:, 0], hits[:, 1], (keep['bin'] & 64) != 0,
         np.where((keep['bin'] & 63) == 0, 0.0, 2.0 ** (((keep['bin'] & 63).astype(int) - 21 + 0.5) / 2))),
        ('reflection', keep['xs'], hits[:, 3], hits[:, 4], None, keep['ds'])):
    mean = float(value.mean())
    bright = value > factor * mean
    r = {'mean': mean, 'bright_count': int(bright.sum()), 'bright_share_of_light': float(value[bright].sum() / value.sum())}
    if bright.any():
        r['largest'] = float(value.max())
        if guided is not None:
            r['guided_share_all'] = float(guided.mean())
            r['guided_share_bright'] = float(guided[bright].mean())
        d = dist[bright]
        r['hit_distance_m_p10_p50_p90'] = [round(float(v), 2) for v in np.percentile(d, [10, 50, 90])]
        hit = bright & (hy >= 0)
        r['bright_with_hit_pixel'] = int(hit.sum())
        rows, light = np.unique(hy[hit].astype(int), return_inverse=True)
        per_row = np.bincount(light, weights=value[hit])
        order = np.argsort(per_row)[::-1][:6]
        total = value[bright].sum()
        r['top_hit_rows_share_of_bright_light'] = {int(rows[i]): round(float(per_row[i] / total), 3) for i in order}
        on_row = hit & (hy >= row0) & (hy <= row1)
        r['share_of_bright_light_from_sill_rows'] = float(value[on_row].sum() / total)
        r['sill_row_hit_columns_p10_p90'] = ([int(v) for v in np.percentile(hx[on_row], [10, 90])]
                                             if on_row.any() else None)
    # Per counted draw (every pixel of the rectangle is a traced surface, so each draw keeps the same count): how many
    # samples hit the sill rows, and their mean value. A draw in which the row holds the sun's light shows as a mean far
    # above the others.
    per_draw = np.prod(np.array(meta['Keeps'][k][2:]) - np.array(meta['Keeps'][k][:2]))
    if len(value) % per_draw == 0:
        draws = len(value) // per_draw
        row_hit = ((hy >= row0) & (hy <= row1)).reshape(draws, per_draw)
        v = value.reshape(draws, per_draw)
        counts = row_hit.sum(axis=1)
        means = np.where(counts > 0, (v * row_hit).sum(axis=1) / np.maximum(counts, 1), 0.0)
        r['sill_row_hits_per_draw_p10_p50_p90'] = [float(x) for x in np.percentile(counts, [10, 50, 90])]
        r['draws_with_sill_row_hits'] = int((counts > 0).sum())
        r['sill_row_hit_mean_by_draw_top5'] = {int(i): [int(counts[i]), round(float(means[i]), 3)]
                                               for i in np.argsort(means)[::-1][:5]}
        r['sill_row_hit_mean_median_draw'] = float(np.median(means[counts > 0])) if (counts > 0).any() else None
        r['share_of_light_from_sill_rows'] = float((v * row_hit).sum() / v.sum())
    out[name] = r
print(json.dumps(out, indent=1))
