"""The model of the many bounces' lag in docs/plans/temporal-renderer.md, section 6, "How the later bounces settle".

Tool code for the design: it never ships, and ticket 6 runs it again when it tunes ClampSigmas and AntiLagShare.
It uses only the standard library, so that it runs with `python3 -I` in the dev shell.

The model treats a room as uniform. Each pixel's diffuse sample is (E + g * h) times a noise factor of mean 1, where E is
the first bounce, g the share of the light that the room sends back per bounce, and h the indirect light of a random hit
pixel: its stored slow (or fast) history of the last frame, or the closed form C in the reset frame. The accumulator is
section 8's: the blend, the fix of short histories, the clamp into the box of the fast histories and the anti-lag, on a
torus of pixels with a still camera. The settled light is E / (1 - g).

Usage: python3 -I docs/research/temporal-renderer/bounce_model.py
"""

import math
import random

SIZE = 32
CAP = 32
FAST_CAP = 4
FIX_FRAMES = 4
CLAMP_SIGMAS = 2.0
ANTI_LAG_SHARE = 0.5
SHOWN_DRAW = 33


def neighbors(size, spacing):
    """The 3 x 3 pixels around each pixel at the spacing, the pixel included, on a torus."""
    table = []
    for y in range(size):
        for x in range(size):
            table.append(
                [((y + dy * spacing) % size) * size + (x + dx * spacing) % size for dy in (-1, 0, 1) for dx in (-1, 0, 1)]
            )
    return table


NEAR = neighbors(SIZE, 1)
FIX_NEIGHBORS = {n: neighbors(SIZE, 2 ** (FIX_FRAMES - 1 - n)) for n in range(1, FIX_FRAMES)}


def simulate(g, noise, start, read="slow", clamp=True, frames=SHOWN_DRAW, seed=1):
    """
    Returns, for each frame from 1, the mean slow history over the pixels as a share of the settled light, and the
    spread of the slow history over the pixels as a share of its mean.
    """
    rng = random.Random(seed)
    pixels = SIZE * SIZE
    first = 1.0
    settled = first / (1 - g)
    slow = [0.0] * pixels
    fast = [0.0] * pixels
    length = [0.0] * pixels
    shape = 1 / (noise * noise) if noise > 0 else 0
    shares = []
    spreads = []
    for frame in range(1, frames + 1):
        source = slow if read == "slow" else fast
        closed = first if start == "closed form" else 0.0
        temporal_slow = [0.0] * pixels
        temporal_fast = [0.0] * pixels
        temporal_length = [0.0] * pixels
        for p in range(pixels):
            hit = closed if frame == 1 else source[rng.randrange(pixels)]
            factor = rng.gammavariate(shape, 1 / shape) if noise > 0 else 1.0
            sample = (first + g * hit) * factor
            n = min(length[p] + 1, CAP)
            temporal_length[p] = n
            temporal_slow[p] = slow[p] + (sample - slow[p]) / n
            temporal_fast[p] = fast[p] + (sample - fast[p]) / min(n, FAST_CAP)
        for p in range(pixels):
            n = temporal_length[p]
            value = temporal_slow[p]
            if n < min(FIX_FRAMES, CAP):
                ring = FIX_NEIGHBORS[int(n)][p]
                weights = sum(temporal_length[q] for q in ring)
                value = sum(temporal_length[q] * temporal_slow[q] for q in ring) / weights
            if clamp and n > FAST_CAP:
                box = [temporal_fast[q] for q in NEAR[p]]
                mean = sum(box) / 9
                sigma = math.sqrt(max(0.0, sum(v * v for v in box) / 9 - mean * mean))
                clamped = min(max(value, mean - CLAMP_SIGMAS * sigma), mean + CLAMP_SIGMAS * sigma)
                lag = abs(value - clamped) / max(mean, 1e-4)
                n = max(FAST_CAP, n * (1 - min(1.0, lag / ANTI_LAG_SHARE)))
                value = clamped
            slow[p] = value
            fast[p] = temporal_fast[p]
            length[p] = n
        mean = sum(slow) / pixels
        shares.append(mean / settled)
        spreads.append(math.sqrt(max(0.0, sum(v * v for v in slow) / pixels - mean * mean)) / max(mean, 1e-9))
    return shares, spreads


def over_seeds(seeds, first, last, **arguments):
    """The mean share and the mean spread over the frames first to last (from 1) and over the seeds."""
    share = spread = 0.0
    for seed in seeds:
        shares, spreads = simulate(seed=seed, frames=last, **arguments)
        share += sum(shares[first - 1 :]) / (last - first + 1)
        spread += sum(spreads[first - 1 :]) / (last - first + 1)
    return share / len(seeds), spread / len(seeds)


def closed_mean(g, start, read="slow", frames=SHOWN_DRAW):
    """The same model without noise and without the clamp: every pixel holds the same value."""
    settled = 1 / (1 - g)
    slow = fast = 0.0
    hit = 1.0 if start == "closed form" else 0.0
    for frame in range(1, frames + 1):
        sample = 1 + g * hit
        slow += (sample - slow) / min(frame, CAP)
        fast += (sample - fast) / min(frame, FAST_CAP)
        hit = slow if read == "slow" else fast
    return slow / settled


def main():
    short = range(1, 9)
    long = range(1, 5)
    for read in ("slow", "fast"):
        print(f"A hit reads the {read} history.")
        print("  Without noise and without the clamp, draw 33 and frame 160:")
        for g in (0.5, 0.8):
            print(
                f"    g = {g}: settled {1 / (1 - g):.0f} E. Closed-form start {closed_mean(g, 'closed form', read):.1%}, "
                f"dark start {closed_mean(g, 'dark', read):.1%}, frame 160 {closed_mean(g, 'closed form', read, 160):.1%}"
            )
        print("  With the clamp and the anti-lag, without noise, draw 33:")
        for g in (0.5, 0.8):
            a = simulate(g, 0, "closed form", read=read)[0][-1]
            b = simulate(g, 0, "dark", read=read)[0][-1]
            print(f"    g = {g}: closed-form start {a:.1%}, dark start {b:.1%}")
        print("  A gamma noise of 25 % per sample, draw 33, the mean of 8 seeds:")
        for g in (0.5, 0.8):
            a = over_seeds(short, SHOWN_DRAW, SHOWN_DRAW, g=g, noise=0.25, start="closed form", read=read)[0]
            b = over_seeds(short, SHOWN_DRAW, SHOWN_DRAW, g=g, noise=0.25, start="closed form", read=read, clamp=False)[0]
            print(f"    g = {g}: with the clamp {a:.1%}, without it {b:.1%}")
        print("  A gamma noise of 100 % per sample (skewed), the mean of 8 seeds at draw 33 and frame 160:")
        for g in (0.5, 0.8):
            still = over_seeds(short, SHOWN_DRAW, SHOWN_DRAW, g=g, noise=1.0, start="closed form", read=read)[0]
            later = over_seeds(short, 160, 160, g=g, noise=1.0, start="closed form", read=read)[0]
            print(f"    g = {g}: draw 33 {still:.1%}, frame 160 {later:.1%}")
        print("  A gamma noise of 100 % per sample (skewed), frames 1000 to 2000, the mean of 4 seeds:")
        for g in (0.0, 0.5, 0.8):
            a = over_seeds(long, 1000, 2000, g=g, noise=1.0, start="closed form", read=read)
            b = over_seeds(long, 1000, 2000, g=g, noise=1.0, start="closed form", read=read, clamp=False)
            print(
                f"    g = {g}: with the clamp {a[0]:.2%} of the settled light, without it {b[0]:.2%}. "
                f"Spread of the slow history {a[1]:.1%}"
            )


if __name__ == "__main__":
    main()
