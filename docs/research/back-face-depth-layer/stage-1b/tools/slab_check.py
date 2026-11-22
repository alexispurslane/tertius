# Throwaway (t1-n8x9): controls 3 and 4 of the probe from one dumped frame.
# python3 -I slab_check.py <dump folder> <frame index> <slab rule: ten|trim|layer> <no-back rule: sheet|plane|ten|solid>
import sys, glob, os
import numpy as np

folder, index, slab, noback = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]

def load(prefix, channels):
    [path] = glob.glob(os.path.join(folder, f"{prefix}-{index}-*"))
    w, h = map(int, path.rsplit("-", 1)[1].split(".")[0].split("x"))
    return np.fromfile(path, dtype=np.float32).reshape(h, w, channels) if channels > 1 else np.fromfile(path, dtype=np.float32).reshape(h, w)

front = load("front", 1)
share = load("back", 1)
finest = load("finest", 2)
level1 = load("level1", 2)
h, w = front.shape
R, G = finest[..., 0], finest[..., 1]
surface = R > 0
withBack = surface & (share > 0)
sheets = surface & ~(share > 0)
backShare = np.float32(1.0 / 1.1)   # glslc folds 1.0 / (1.0 + 0.1) in double: 0.909090936
print(f"size {w}x{h}: surface pixels {surface.sum()}, with a back face {withBack.sum()}, sheets {sheets.sum()}")
print(f"sky pixels with G != 0: {int(((~surface) & (G != 0)).sum())}")

# Control 3: the finest level's slab against the rule.
if slab == "layer":
    rel = np.abs(G[withBack] / R[withBack] - share[withBack]) / share[withBack]
    print(f"with a back face: |G/R - share| / share: max {rel.max():.3e}, 99.9th percentile {np.percentile(rel, 99.9):.3e}")
    expect = {"sheet": R, "ten": R * backShare, "solid": np.zeros_like(R)}.get(noback)
    if expect is not None:
        print(f"sheets: G != the rule's back w on {int((G[sheets] != expect[sheets]).sum())} of {sheets.sum()}")
    else:
        print(f"sheets (plane): G > R on {int((G[sheets] > R[sheets]).sum())}, G < R on {int((G[sheets] < R[sheets]).sum())} of {sheets.sum()}")
elif slab == "ten":
    print(f"G != R * (1 / 1.1) on {int((G[surface] != R[surface] * backShare).sum())} of {surface.sum()}")
elif slab == "trim":
    expect = np.maximum(R * backShare, R * share)
    print(f"G != max(R / 1.1, R * share) on {int((G[surface] != expect[surface]).sum())} of {surface.sum()}")
print(f"G > R on {int((G[surface] > R[surface]).sum())} surface pixels")

# The slab in m along the view ray: the front distance times (R / G - 1), for pixels with a back face.
slabM = np.where(withBack, front * (R / np.where(G > 0, G, 1) - 1), np.nan)
sill = withBack & (front >= 39.45) & (front <= 39.7)
if sill.any():
    v = slabM[sill]
    print(f"pixels at 39.45 to 39.7 m with a back face: {sill.sum()}, slab median {np.median(v):.3f} m, "
          f"25th {np.percentile(v, 25):.3f} m, 75th {np.percentile(v, 75):.3f} m")
    rows = np.where(sill.any(axis=1))[0]
    print(f"  their rows {rows.min()} to {rows.max()}")

# Control 4: each cell of level 1 holds the slab of every pixel under it: its G is at most each pixel's G, and its R at
# least each pixel's R, for the pixels that see a surface.
lh, lw = (h + 1) // 2, (w + 1) // 2
L = level1[:lh, :lw]
pad = np.zeros((lh * 2, lw * 2, 2), dtype=np.float32)
pad[..., 1] = np.inf
pad[:h, :w] = finest
pad[:h, :w, 1] = np.where(surface, G, np.inf)
childG = pad[..., 1].reshape(lh, 2, lw, 2).min(axis=(1, 3))
childR = pad[..., 0].reshape(lh, 2, lw, 2).max(axis=(1, 3))
filled = childR > 0
print(f"level 1 ({lw}x{lh} of the mip {level1.shape[1]}x{level1.shape[0]}): cells with a surface {filled.sum()}")
print(f"  cells whose G > the smallest G under it: {int((L[..., 1][filled] > childG[filled]).sum())}")
print(f"  cells whose R < the largest R under it: {int((L[..., 0][filled] < childR[filled]).sum())}")
ratio = L[..., 1][filled] / childG[filled]
print(f"  cell G over the smallest pixel G: min {ratio.min():.4f}, median {np.median(ratio):.4f}")
