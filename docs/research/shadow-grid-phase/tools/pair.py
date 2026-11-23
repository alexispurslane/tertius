# Tool code (t1-gout, step 3). pair.py <main.png> <fix.png> <out.png> <title> [x y size]: main beside the fix, at half size
# whole and at 1:1 in a box of <size> pixels (default 256) at (x, y), or else at the box whose summed luminance difference
# is largest, with the difference at a gain of 8 (grey 0). Prints the box, the count of pixels whose display-linear
# luminance differs by more than 1 % of the 99th percentile, and the largest difference.
import sys
import numpy as np
from PIL import Image, ImageDraw

a_path, b_path, out, title = sys.argv[1:5]
A = np.asarray(Image.open(a_path).convert('RGB')).astype(float) / 255
B = np.asarray(Image.open(b_path).convert('RGB')).astype(float) / 255
lin = lambda a: np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]
d = lin(B) - lin(A)
p99 = max(float(np.percentile(lin(A), 99)), 1e-4)
S = int(sys.argv[7]) if len(sys.argv) > 7 else 256
if len(sys.argv) > 6:
    x0, y0 = int(sys.argv[5]), int(sys.argv[6])
else:
    k = np.pad(np.abs(d).cumsum(0).cumsum(1), ((1, 0), (1, 0)))
    sums = k[S:, S:] - k[:-S, S:] - k[S:, :-S] + k[:-S, :-S]
    y0, x0 = (int(v) for v in np.unravel_index(np.argmax(sums), sums.shape))
h, w = A.shape[:2]
half = lambda a: Image.fromarray((a * 255).astype(np.uint8)).resize((w // 2, h // 2), Image.LANCZOS)
crop = lambda a: Image.fromarray((a[y0:y0 + S, x0:x0 + S] * 255).astype(np.uint8))
g = np.clip(0.5 + 8 * d / p99, 0, 1)
gap = 12
sheet = Image.new('RGB', (w + gap, h // 2 + S + 3 * gap + 40), 'white')
sheet.paste(half(A), (0, 30)); sheet.paste(half(B), (w // 2 + gap, 30))
y1 = h // 2 + 30 + gap
sheet.paste(crop(A), (0, y1)); sheet.paste(crop(B), (S + gap, y1))
sheet.paste(Image.fromarray((g[y0:y0 + S, x0:x0 + S] * 255).astype(np.uint8)).convert('RGB'), (2 * (S + gap), y1))
dr = ImageDraw.Draw(sheet)
for im in (0, 1):
    dr.rectangle([im * (w // 2 + gap) + x0 // 2, 30 + y0 // 2, im * (w // 2 + gap) + (x0 + S) // 2, 30 + (y0 + S) // 2],
                 outline=(255, 60, 60))
dr.text((4, 8), f'{title}: main left, the fix right; below at 1:1 from ({x0}, {y0}): main, the fix, difference x8',
        fill='black')
sheet.save(out)
print(f'box ({x0}, {y0}) {S}px; changed {int((np.abs(d) > 0.01 * p99).sum())} px; largest {np.abs(d).max() / p99:.3f} of p99')
