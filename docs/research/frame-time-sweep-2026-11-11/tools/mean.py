"""mean.py <folder> <prefix> <first frame>: the mean of the frames <prefix>-NNNNNN.png from the first frame on, in
display-linear RGB, written as <folder>/<prefix>-mean.npy and .png. The long reference of a film whose history keeps growing."""
import sys, glob, numpy as np
from PIL import Image
folder, prefix, first = sys.argv[1], sys.argv[2], int(sys.argv[3])
frames = sorted(glob.glob(f"{folder}/{prefix}-*.png"))[first:]
def lin(p):
    a = np.asarray(Image.open(p).convert("RGB")).astype(np.float64) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
mean = sum(lin(p) for p in frames) / len(frames)
np.save(f"{folder}/{prefix}-mean.npy", mean.astype(np.float32))
enc = np.where(mean <= 0.0031308, mean * 12.92, 1.055 * mean ** (1 / 2.4) - 0.055)
Image.fromarray((enc * 255 + 0.5).astype(np.uint8)).save(f"{folder}/{prefix}-mean.png")
print(f"{prefix}: mean of {len(frames)} frames")
