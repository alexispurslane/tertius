# Tool code of t1-nyjl: the right wall's vertical bands in the diffuse picture. Shows, stretched 12 times around each
# image's own blur at 36 px, the diffuse capture 19, the diffuse reference (the mean of captures 30 to 99 of the
# 100-capture diffuse take) and the reference rounded to 8 bits, and prints the column profile's band (mean over rows of
# gauss(img, 3) - gauss(img, 36), relative) std for each. Usage: python3 -I bands.py <out png>
import sys
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter
W = [0.2126, 0.7152, 0.0722]
x0, y0, x1, y1 = 1300, 100, 1800, 500
lin = lambda a: np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
cap = lin(np.asarray(Image.open('/private/tmp/t1-wl7c/guide/stills/bay-diffuse-0.25/c-s19.png').convert('RGB')) / 255) @ W
refrgb = np.load('/private/tmp/t1-wl7c/guide/daub-diffuse/reference-mean.npy').astype(np.float64); ref = refrgb @ W
enc = np.where(refrgb <= 0.0031308, refrgb * 12.92, 1.055 * np.clip(refrgb, 0, 1) ** (1 / 2.4) - 0.055)
q = lin(np.round(enc * 255) / 255) @ W
panels = []
for lab, img in (('diffuse capture 19', cap), ('diffuse reference (70 captures)', ref), ('reference rounded to 8 bits', q)):
    c = img[y0:y1, x0:x1]; rel = c / gaussian_filter(c, 36) - 1
    prof = (gaussian_filter(c, 3) / gaussian_filter(c, 36) - 1).mean(0)
    print(f'{lab}: column profile band std {prof.std():.5f}, relative band std {rel.std():.5f}')
    panels.append((lab, np.clip(0.5 + 12 * rel, 0, 1)))
h, w = y1 - y0, x1 - x0
sheet = Image.new('RGB', (3 * w + 16, h + 22), 'white'); d = ImageDraw.Draw(sheet)
for i, (lab, a) in enumerate(panels):
    sheet.paste(Image.fromarray((a * 255).astype(np.uint8)).convert('RGB'), (i * (w + 8), 22)); d.text((i * (w + 8) + 4, 5), lab + ', x12 around its 36 px blur', fill='black')
sheet.save(sys.argv[1])
