"""Research tool (issue #42): reads the scene-linear HDR readback (volume.bin.hdr, before the tone map) of probe and lamp
stills. For each capture folder it prints the whole frame's mean RGB and the receiver region's mean RGB with its
saturation, (max - min) / max, in W/(m^2 sr). The receiver region is the panel left of the slabs or the wall: x from
1/16 to 5/8 and y from 1/9 to 5/6 of the frame, as the region "receiver" of crossing-steps.py.
usage: stills.py <capture folder>..."""
import sys
from pathlib import Path

import numpy as np


def load(folder):
    a = np.fromfile(Path(folder) / "volume.bin.hdr", dtype=np.float32)
    n = a.size // 4
    w = int(round((n * 16 / 9) ** 0.5))
    return a.reshape(n // w, w, 4)[..., :3]


def saturation(rgb):
    return (rgb.max() - rgb.min()) / max(rgb.max(), 1e-12)


for folder in sys.argv[1:]:
    hdr = load(folder)
    h, w, _ = hdr.shape
    frame = hdr.reshape(-1, 3).mean(0)
    receiver = hdr[h // 9:5 * h // 6, w // 16:5 * w // 8].reshape(-1, 3).mean(0)
    print(f"{folder}: {w}x{h}, frame mean RGB ({frame[0]:.4f}, {frame[1]:.4f}, {frame[2]:.4f}); receiver mean RGB "
          f"({receiver[0]:.5f}, {receiver[1]:.5f}, {receiver[2]:.5f}), saturation {saturation(receiver):.2f}")
