# Tool code of t0-5tyj. slitedge.py <out.png> <label=image>...: the left edge of the sun's patch on the left wall of the
# film bay-sunbeam (the bright slit at the top left), at full output size (1920 x 1080). Per image and per output row
# from FIRST to LAST: the edge is the leftmost column of the crop whose 8-bit luma reaches BRIGHT. The teeth are the
# edge's residual from its own running mean over SMOOTH rows (the edge bends, so a straight line does not fit). It prints the residual's spread
# (highest minus lowest, and the 95th minus the 5th percentile) and the rows between the residual's upward steps of at
# least 2 columns (the teeth's period along the edge), in output pixels; a render pixel at the scale 2/3 is 1.5 output
# pixels. The picture is the crop X0..X1, Y0..Y1 of each image, side by side, scaled up 3 times by nearest neighbor.
import sys
import numpy as np
from PIL import Image, ImageDraw

X0, X1, Y0, Y1, FIRST, LAST, BRIGHT, ZOOM, SMOOTH = 150, 420, 0, 360, 4, 330, 250, 3, 31
out, args = sys.argv[1], [a.split('=', 1) for a in sys.argv[2:]]
tiles = []
for label, path in args:
    im = Image.open(path).convert('RGB')
    luma = np.asarray(im.convert('L')).astype(int)
    rows = np.arange(FIRST, LAST + 1)
    edge = np.array([np.argmax(luma[y, :600] >= BRIGHT) for y in rows], dtype=float)
    half = SMOOTH // 2
    padded = np.pad(edge, half, mode='reflect')
    fit = np.convolve(padded, np.ones(SMOOTH) / SMOOTH, mode='valid')
    res = (edge - fit)[half:-half]
    rows = rows[half:-half]
    steps = np.nonzero(np.diff(res) >= 2)[0]
    gaps = np.diff(rows[steps]) if len(steps) > 1 else np.array([])
    print(f'{label:10s} rows {rows[0]} to {rows[-1]}: residual spread {res.max() - res.min():.1f} px, '
          f'5 to 95 % {np.percentile(res, 95) - np.percentile(res, 5):.1f} px; {len(steps)} upward steps of >= 2 px, '
          f'rows between them: median {np.median(gaps) if len(gaps) else float("nan"):.0f}, '
          f'from {gaps.min() if len(gaps) else "-"} to {gaps.max() if len(gaps) else "-"}')
    crop = im.crop((X0, Y0, X1, Y1)).resize(((X1 - X0) * ZOOM, (Y1 - Y0) * ZOOM), Image.NEAREST)
    tile = Image.new('RGB', (crop.width, crop.height + 24), 'black')
    tile.paste(crop, (0, 24))
    ImageDraw.Draw(tile).text((6, 6), f'{label}: x {X0}-{X1}, y {Y0}-{Y1}, {ZOOM}x nearest', fill='white')
    tiles.append(tile)
sheet = Image.new('RGB', (sum(t.width for t in tiles) + 8 * (len(tiles) - 1), tiles[0].height), (60, 60, 60))
x = 0
for t in tiles:
    sheet.paste(t, (x, 0))
    x += t.width + 8
sheet.save(out)
print(out, sheet.size)
