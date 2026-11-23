# Tool code of ter-yglw: a picture of the six faces of one map from a face dump (FaceProbe.Dump: level 0 of the radiance
# cube, RGBA half floats per texel, row by row, one file per face). The faces lie in one row in layer order (+X, -X, +Y,
# -Y, +Z, -Z), each scaled up with the nearest texel to <px> pixels, so that a 32 face and a 128 face show at the same
# size. The light maps to display values by 2^<stops> and the Hable curve's shape x / (1 + x), then sRGB.
# With several dump dirs, each makes one row, top to bottom, 4 px apart.
# Usage: python3 -I faceview.py <map> <stops> <px> <out png> <dump dir>...
import sys
from pathlib import Path
import numpy as np
from PIL import Image

m, stops, px, out = int(sys.argv[1]), float(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
dumps = [Path(d) for d in sys.argv[5:]]
tiles = []
for dump in dumps:
  for face in range(6):
    raw = np.fromfile(dump / f'map{m}-face{face}.f16', '<f2').astype(np.float32)
    w = int(round((raw.size / 4) ** 0.5))
    rgb = np.nan_to_num(raw.reshape(w, w, 4)[..., :3], nan=0.0, posinf=0.0) * 2.0 ** stops
    rgb = rgb / (1.0 + rgb)
    srgb = np.where(rgb <= 0.0031308, 12.92 * rgb, 1.055 * np.power(np.maximum(rgb, 0), 1 / 2.4) - 0.055)
    img = Image.fromarray((np.clip(srgb, 0, 1) * 255).astype(np.uint8)).resize((px, px), Image.NEAREST)
    tiles.append(img)
sheet = Image.new('RGB', (6 * px + 5 * 4, len(dumps) * px + (len(dumps) - 1) * 4), (255, 255, 255))
for i, t in enumerate(tiles):
    sheet.paste(t, ((i % 6) * (px + 4), (i // 6) * (px + 4)))
sheet.save(out)
