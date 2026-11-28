"""Research tool (issue #42): measures the steps between neighboring frames of a crossing film, as a person sees them.
It decodes the film's displayed 8-bit sRGB frames with ffmpeg and reads two regions of the receiver panel, which the
slabs do not cover. For each pair of neighboring frames it computes the change of the region's mean (8-bit levels, the
largest channel) and the largest change of one pixel in one channel (8-bit levels). The film holds two frames per
camera position, so a pair inside one position shows the frame-to-frame noise and a pair across two positions shows
the camera's own change too.
usage: crossing-steps.py <film.mkv> [<film.mkv>...]
Prints, per film: the largest region-mean step with its frame, the largest per-pixel step with its frame, the same at
the position changes near the 0.25 m grid's step (x = 0.12 to 0.13 m), and the mean color of the top band
before and after the grid's step."""
import subprocess
import sys

import numpy as np

WIDTH, HEIGHT = 640, 360
# x0, y0, x1, y1 in the 640 x 360 frame. "receiver" is the panel left of the slabs. "top" is the band at the top of
# the receiver where main shows a red patch at the grid's step.
REGIONS = {"receiver": (40, 40, 400, 300), "top": (40, 8, 400, 60)}
# Frame n shows camera position order[n // 2]: out from x = 0 to 0.24 m in 97 positions, and back.
ORDER = list(range(97)) + list(range(96, -1, -1))


def frames(path):
    raw = subprocess.run(["ffmpeg", "-loglevel", "error", "-i", path, "-f", "rawvideo", "-pix_fmt", "rgb24", "-"],
                         check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.uint8).reshape(-1, HEIGHT, WIDTH, 3).astype(np.float32)


def crop(f, region):
    x0, y0, x1, y1 = region
    return f[:, y0:y1, x0:x1]


def main():
    for path in sys.argv[1:]:
        f = frames(path)
        print(f"{path}: {len(f)} frames")
        for name, region in REGIONS.items():
            c = crop(f, region)
            means = c.reshape(len(c), -1, 3).mean(1)
            mean_step = np.abs(np.diff(means, axis=0)).max(1)
            pixel_step = np.abs(np.diff(c, axis=0)).reshape(len(c) - 1, -1).max(1)
            i, j = int(mean_step.argmax()), int(pixel_step.argmax())
            print(f"  {name}: largest mean step {mean_step[i]:.2f} levels at frames {i}-{i+1} "
                  f"(positions {ORDER[i//2]}-{ORDER[(i+1)//2]}); largest pixel step {pixel_step[j]:.0f} levels at "
                  f"frames {j}-{j+1}")
            # Frame pairs between two positions from 48 to 52 (x = 0.12 to 0.13 m): the 0.25 m grid steps there.
            near = [n for n in range(len(f) - 1)
                    if ORDER[n // 2] != ORDER[(n + 1) // 2] and 48 <= min(ORDER[n // 2], ORDER[(n + 1) // 2]) < 52]
            for n in near:
                print(f"    positions {ORDER[n//2]}-{ORDER[(n+1)//2]} (frames {n}-{n+1}): mean step {mean_step[n]:.2f}, "
                      f"pixel step {pixel_step[n]:.0f}")
            quiet = [n for n in range(len(f) - 1) if n % 2 == 0]
            print(f"    noise (pairs inside one position): mean step up to {mean_step[quiet].max():.2f}, "
                  f"pixel step up to {pixel_step[quiet].max():.0f}")
            if name == "top":
                before, after = means[ORDER.index(48) * 2], means[ORDER.index(52) * 2]
                print(f"    top band mean RGB before the step {np.round(before, 1)}, after {np.round(after, 1)}")


if __name__ == "__main__":
    main()
