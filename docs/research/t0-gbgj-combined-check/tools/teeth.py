# Tool code of t0-gbgj. teeth.py <out.png> <first frame> <count> <label=run>...: the saw teeth on the left edge of the sun's
# slit in the film bay-sunbeam (bug tc-6v3d), from the frames that film.py keeps in <run>/frames (m.sh runs it in $W/w,
# where s/<run> links each run). Frame k is the film's frame k, counted from 0 as the survey counts it.
#   The picture: for each run, <count> frames from <first frame> in a row, each the crop x 150 to 420, y 0 to 360 of the
#   1920 x 1080 output at 3x nearest neighbor, labels on top. One row of tiles per run.
#   The edge: in each row y of the crop, the first x (from the left) where the display-linear luminance passes half of the
#   way from the wall (the crop's 10th percentile) to the slit (its 99th percentile), in output pixels, with linear
#   interpolation between pixels. Rows without such a crossing in x 150 to 420 count as no edge.
#   A tooth: the edge's offset from a straight line fitted to rows 120 to 340 (the slit's lower edge, where the teeth sit),
#   positive where the slit reaches further left. LIMIT: the line also counts a bend of the edge as teeth (frames 104 to
#   107 show a smooth bend that it reads as teeth 4 to 9 pixels deep), and a running median of 41 rows reads the slit's
#   corner near row 120 as a tooth in every frame. So read the numbers only where the picture shows teeth. A tooth tip is a local maximum of that offset above 1.5 pixels, the
#   largest within 5 rows. Printed: per frame, the tips' rows and depths; between frames in a row, the correlation of
#   their offsets and the shift in rows that correlates best (-9 to 9): 0 means that the teeth keep their rows.
import glob, sys
import numpy as np
from PIL import Image, ImageDraw
X0, Y0, X1, Y1, ZOOM, FIT = 150, 0, 420, 360, 3, (120, 340)
def lin(p):
    a = np.asarray(Image.open(p).convert('RGB').crop((X0, Y0, X1, Y1))).astype(float) / 255
    a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
    return a @ [0.2126, 0.7152, 0.0722]
def edge(y):
    lo, hi = np.percentile(y, 10), np.percentile(y, 99)
    t = (lo + hi) / 2
    xs = np.full(y.shape[0], np.nan)
    for r in range(y.shape[0]):
        k = np.nonzero(y[r] > t)[0]
        if len(k) and k[0] > 0:
            a, b = y[r, k[0] - 1], y[r, k[0]]
            xs[r] = X0 + k[0] - 1 + (t - a) / (b - a)
    return xs
def offset(xs):
    rows = np.arange(FIT[0], FIT[1]); ok = ~np.isnan(xs[rows])
    line = np.polyval(np.polyfit(rows[ok], xs[rows][ok], 1), rows)
    return rows, line - xs[rows]
def tips(rows, off):
    out = []
    for i in range(len(off)):
        w = off[max(0, i - 5): i + 6]
        if off[i] > 1.5 and off[i] == np.nanmax(w): out.append((int(rows[i]), round(float(off[i]), 1)))
    return out
def main():
    out, first, count = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
    sheet_rows = []
    for arg in sys.argv[4:]:
        label, run = arg.split('=', 1)
        files = sorted(glob.glob(f's/{run}/frames/m*-*.png'))
        offs = []
        tiles = []
        for k in range(first, first + count):
            rows, off = offset(edge(lin(files[k])))
            offs.append(np.nan_to_num(off))
            t = tips(rows, off)
            gaps = np.diff([r for r, _ in t])
            print(f"{label} frame {k}: {len(t)} tips, rows/depths {t}, gaps {list(gaps)}, mean depth "
                  f"{np.mean([d for _, d in t]) if t else 0:.1f} px")
            im = Image.open(files[k]).convert('RGB').crop((X0, Y0, X1, Y1))
            im = im.resize((im.width * ZOOM, im.height * ZOOM), Image.NEAREST)
            tile = Image.new('RGB', (im.width, im.height + 22)); tile.paste(im, (0, 22))
            ImageDraw.Draw(tile).text((4, 4), f'{label} frame {k}: x {X0}-{X1}, y {Y0}-{Y1}, 3x nearest', fill=(255, 255, 255))
            tiles.append(tile)
        for a, b, k in zip(offs, offs[1:], range(first, first + count)):
            best = max(range(-9, 10), key=lambda s: np.corrcoef(a[9:-9], np.roll(b, s)[9:-9])[0, 1])
            print(f"{label} frames {k} and {k + 1}: correlation {np.corrcoef(a, b)[0, 1]:.2f}, best shift {best} rows")
        row = Image.new('RGB', (sum(t.width + 6 for t in tiles), tiles[0].height), (40, 40, 40)); x = 0
        for t in tiles: row.paste(t, (x, 0)); x += t.width + 6
        sheet_rows.append(row)
    sheet = Image.new('RGB', (max(r.width for r in sheet_rows), sum(r.height + 6 for r in sheet_rows)), (40, 40, 40)); y = 0
    for r in sheet_rows: sheet.paste(r, (0, y)); y += r.height + 6
    sheet.save(out); print(out)


if __name__ == '__main__':
    main()
