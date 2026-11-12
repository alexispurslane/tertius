# Research prototype for ticket ter-s97m: checks an STBN mask from stbn.c against white noise.
#   python3 -I spectra.py <mask.bin> [<plot.png>]
# Prints, for the radial value r and for a smooth function of both values (the cosine draw's direction x), the
# spatial power at low frequencies against white noise, the variance left after a Gaussian blur of each frame, and the
# variance of each pixel's mean over windows of n consecutive frames, each as a ratio to white noise.
import sys
import numpy as np
from scipy.ndimage import gaussian_filter

W = H = 128
T = 64


def load(path):
    raw = np.fromfile(path, np.uint8).reshape(8 * H, 8 * W, 2)
    frames = raw.reshape(8, H, 8, W, 2).transpose(0, 2, 1, 3, 4).reshape(T, H, W, 2)
    return (frames.astype(np.float64) + 0.5) / 256.0


def direction_x(v):
    # The cosine draw's tangent component, sqrt(r) cos(2 pi a): a smooth function of both values.
    return np.sqrt(v[..., 0]) * np.cos(2 * np.pi * v[..., 1])


def report(name, f, white):
    def radial(x):
        p = np.abs(np.fft.fft2(x - x.mean(axis=(1, 2), keepdims=True))) ** 2
        p = p.mean(axis=0)
        fy, fx = np.meshgrid(np.fft.fftfreq(H), np.fft.fftfreq(W), indexing="ij")
        rad = np.hypot(fx, fy)
        bins = [(0.0, 0.05), (0.05, 0.1), (0.1, 0.2), (0.2, 0.35), (0.35, 0.71)]
        return [p[(rad > lo) & (rad <= hi)].mean() for lo, hi in bins]

    rf, rw = radial(f), radial(white)
    bands = "  ".join(f"{b:.2f}" for b in np.array(rf) / np.array(rw))
    blur = [gaussian_filter(f, (0, s, s), mode="wrap").var(axis=(1, 2)).mean()
            / gaussian_filter(white, (0, s, s), mode="wrap").var(axis=(1, 2)).mean() for s in (1, 2, 4)]
    windows = []
    for n in (4, 8, 16, 32, 64):
        m = np.stack([np.roll(f, -s, axis=0)[:n].mean(axis=0) for s in range(0, T, 4)])
        mw = np.stack([np.roll(white, -s, axis=0)[:n].mean(axis=0) for s in range(0, T, 4)])
        windows.append(m.var(axis=(1, 2)).mean() / mw.var(axis=(1, 2)).mean())
    print(f"{name:12s} spatial power / white in radial bands 0-.05 .05-.1 .1-.2 .2-.35 .35-.71: {bands}")
    print(f"{'':12s} blurred variance / white at sigma 1, 2, 4 px: " + "  ".join(f"{b:.3f}" for b in blur))
    print(f"{'':12s} variance of a pixel's mean over 4, 8, 16, 32, 64 frames / white: "
          + "  ".join(f"{w:.3f}" for w in windows))


def main():
    v = load(sys.argv[1])
    rng = np.random.default_rng(1)
    white = rng.random(v.shape)
    report("r", v[..., 0], white[..., 0])
    report("direction x", direction_x(v), direction_x(white))
    # The same seen from the block grid of a 2 x 2 block's ray at stride 4 in time is not needed: the block's mask
    # is read every draw. A pixel's own corner turn reads every 4th frame: its subset over the period.
    sub = v[::4]
    print("every 4th frame, a pixel's mean over 16 frames / white:",
          f"{direction_x(sub).mean(axis=0).var() / direction_x(white[::4]).mean(axis=0).var():.3f}")
    if len(sys.argv) > 2:
        from PIL import Image
        tiles = np.concatenate([np.concatenate([(v[t, ..., 0] * 255).astype(np.uint8) for t in range(4)], axis=1),
                                np.concatenate([(white[t, ..., 0] * 255).astype(np.uint8) for t in range(4)], axis=1)])
        Image.fromarray(tiles).resize((tiles.shape[1] * 2, tiles.shape[0] * 2), Image.NEAREST).save(sys.argv[2])


main()
