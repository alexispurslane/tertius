#!/usr/bin/env python3
# Tool code of ter-fe63's stage 2. Compares the takes of takes.sh, unpatched main against the production branch, with the
# measures of stage 1 (tools/analyze_real_cuts.py and tools/step2_analyze.py of the stage 1 record):
#   specks: pixels of the nose's ceiling cone [0:500, 480:1880] whose 601 luma is more than 12 above the median of their
#           7 x 7 neighbourhood, outside the lamps (luma above 200, widened by 13 pixels);
#   floor grain: the RMS of the floor box's linear luminance less its Gaussian blur of sigma 2, over the box's mean;
#   flicker: the frame-to-frame RMS of the box's 601 luma, and the spread of each pixel over the frames.
# Writes summary.json into <out>/tables, and side-by-side films (H.264 CRF 12, 60 fps) and 1:1 strips into <out>/pics.
# Usage: python3 -I analyze.py <takes dir> <out dir>
import hashlib
import json
import subprocess
import sys
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, maximum_filter, median_filter

TAKES, OUT = Path(sys.argv[1]), Path(sys.argv[2])
PICS, TABLES = OUT / "pics", OUT / "tables"
WIDTH, HEIGHT = 1920, 1080
NOSE_BOX = (750, 0, 1170, 330)
CONE_BOX = (640, 0, 1280, 360)
FLOOR_BOX = (600, 620, 1320, 1000)
FLOOR_STRIP_BOX = (760, 660, 1160, 960)
# Frames 0..59 film the aft view, frame 60 the far view, and frame 61 is the first draw after the real cut.
CUT_FRAME = 61
POST_DRAWS = 65
LUMINANCE = np.array([0.2126, 0.7152, 0.0722])


def frames_of(film: Path) -> list:
    decoder = subprocess.Popen(["ffmpeg", "-v", "error", "-i", str(film), "-f", "rawvideo", "-pix_fmt", "rgb24", "-"],
                               stdout=subprocess.PIPE)
    size = WIDTH * HEIGHT * 3
    frames = []
    while len(buffer := decoder.stdout.read(size)) == size:
        frames.append(np.frombuffer(buffer, dtype=np.uint8).reshape(HEIGHT, WIDTH, 3))
    decoder.stdout.close()
    decoder.wait()
    return frames


def luma(rgb: np.ndarray) -> np.ndarray:
    return (rgb[..., 0] * 299.0 + rgb[..., 1] * 587.0 + rgb[..., 2] * 114.0) / 1000.0


def linear_luminance(rgb: np.ndarray) -> np.ndarray:
    a = rgb.astype(np.float64) / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ LUMINANCE


def specks_of_gray(gray: np.ndarray) -> int:
    lamps = maximum_filter(gray > 200.0, size=13)
    return int(((gray - median_filter(gray, size=7) > 12.0) & ~lamps).sum())


def specks(frames: list) -> list:
    grays = [luma(f[0:500, 480:1880].astype(np.float32)) for f in frames]
    with ProcessPoolExecutor(max_workers=6) as pool:
        return list(pool.map(specks_of_gray, grays, chunksize=8))


def crop(frame, box):
    x0, y0, x1, y1 = box
    return frame[y0:y1, x0:x1]


def grain(frames: list, box) -> list:
    out = []
    for frame in frames:
        y = linear_luminance(crop(frame, box))
        out.append(float(np.sqrt(((y - gaussian_filter(y, 2)) ** 2).mean()) / y.mean()))
    return out


def flicker(frames: list, box) -> dict:
    g = np.stack([luma(crop(f, box).astype(np.float64)) for f in frames])
    return {
        "frames": len(frames),
        "frame_to_frame_rms": float(np.sqrt((np.diff(g, axis=0) ** 2).mean())),
        "mean_temporal_std_per_pixel": float(g.std(0).mean()),
        "box_mean_gray": float(g.mean()),
    }


def spread(values) -> dict:
    v = np.asarray(values, dtype=np.float64)
    return {"median": float(np.median(v)), "mean": float(v.mean()), "p95": float(np.percentile(v, 95)),
            "max": float(v.max())}


def side_by_side(takes: dict, box, first: int, end: int, out: Path, note=None, scale: int = 1):
    x0, y0, x1, y1 = box
    w, h = (x1 - x0) // scale, (y1 - y0) // scale
    pad, header = 8, 22
    width = len(takes) * w + (len(takes) - 1) * pad
    width += width % 2
    height = h + header + (h + header) % 2
    encoder = subprocess.Popen(["ffmpeg", "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24",
                                "-s", f"{width}x{height}", "-r", "60", "-i", "-", "-c:v", "libx264", "-crf", "12",
                                "-pix_fmt", "yuv420p", str(out)], stdin=subprocess.PIPE)
    for k in range(first, end):
        canvas = Image.new("RGB", (width, height), "white")
        draw = ImageDraw.Draw(canvas)
        for j, (label, frames) in enumerate(takes.items()):
            image = Image.fromarray(np.ascontiguousarray(crop(frames[k], box)))
            if scale != 1:
                image = image.resize((w, h), Image.BOX)
            canvas.paste(image, (j * (w + pad), header))
            draw.text((j * (w + pad) + 4, 5), f"{label}  frame {k}  {note(label, k) if note else ''}", fill="black")
        encoder.stdin.write(canvas.tobytes())
    encoder.stdin.close()
    assert encoder.wait() == 0, out


def strip(takes: dict, box, draws: list, out: Path, note=None):
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    pad, header, title = 6, 22, 20
    canvas = Image.new("RGB", (len(draws) * (w + pad) - pad, len(takes) * (h + header + title + pad) - pad), "white")
    draw = ImageDraw.Draw(canvas)
    for r, (label, frames) in enumerate(takes.items()):
        top = r * (h + header + title + pad)
        draw.text((4, top + 3), label, fill="black")
        for c, d in enumerate(draws):
            canvas.paste(Image.fromarray(np.ascontiguousarray(crop(frames[CUT_FRAME + d], box))),
                         (c * (w + pad), top + title + header))
            draw.text((c * (w + pad) + 3, top + title + 4), f"d{d} {note(label, d) if note else ''}", fill="black")
    canvas.save(out)


def framemd5(film: Path) -> list:
    text = subprocess.check_output(["ffmpeg", "-v", "error", "-i", str(film), "-f", "framemd5", "-"], text=True)
    return [line.rsplit(",", 1)[1].strip() for line in text.splitlines() if not line.startswith("#")]


def pair(name: str) -> dict:
    return {"main": frames_of(TAKES / f"{name}-main/film.mkv"), "branch": frames_of(TAKES / f"{name}-branch/film.mkv")}


def main():
    PICS.mkdir(parents=True, exist_ok=True)
    TABLES.mkdir(parents=True, exist_ok=True)
    summary = {}

    # The mirror plate's still: the ramp keeps every pixel of roughness 0.05 off the guided draw.
    plate = {b: np.asarray(Image.open(next((TAKES / f"plate-{b}").glob("*.png"))).convert("RGB")).astype(np.int16)
             for b in ("main", "branch")}
    summary["plate"] = {"identical": bool((plate["main"] == plate["branch"]).all()),
                        "max_abs_difference": int(np.abs(plate["main"] - plate["branch"]).max())}
    print("plate", summary["plate"])

    # Settled films: the sunlit nose and rect-lamps' glossy floor, 360 frames each.
    nose = pair("nose-film")
    nose_specks = {b: specks(f) for b, f in nose.items()}
    summary["nose_film"] = {
        "specks_frames_0_29": {b: spread(v[:30]) for b, v in nose_specks.items()},
        "specks_frames_120_359": {b: spread(v[120:]) for b, v in nose_specks.items()},
        "flicker_nose_box_120_359": {b: flicker(f[120:], NOSE_BOX) for b, f in nose.items()},
    }
    side_by_side(nose, CONE_BOX, 0, 360, PICS / "nose-film-main-branch.mp4",
                 note=lambda b, k: f"specks {nose_specks[b][k]}")
    for b, f in nose.items():
        Image.fromarray(f[300]).save(PICS / f"nose-film-{b}-frame300.png")
    print("nose film", json.dumps(summary["nose_film"]["specks_frames_120_359"]))
    del nose

    lamps = pair("lamps-film")
    lamps_grain = {b: grain(f, FLOOR_BOX) for b, f in lamps.items()}
    summary["lamps_film"] = {
        "floor_grain_frames_0_29": {b: spread(v[:30]) for b, v in lamps_grain.items()},
        "floor_grain_frames_120_359": {b: spread(v[120:]) for b, v in lamps_grain.items()},
        "flicker_floor_box_120_359": {b: flicker(f[120:], FLOOR_BOX) for b, f in lamps.items()},
    }
    side_by_side(lamps, FLOOR_BOX, 0, 360, PICS / "lamps-film-main-branch.mp4",
                 note=lambda b, k: f"grain {lamps_grain[b][k]:.4f}")
    for b, f in lamps.items():
        Image.fromarray(f[300]).save(PICS / f"lamps-film-{b}-frame300.png")
    print("lamps film", json.dumps(summary["lamps_film"]["floor_grain_frames_120_359"]))
    del lamps

    # Real cuts: 60 aft frames, one far frame, then 65 draws after the cut.
    cut = pair("cut-nose")
    assert all(len(f) == CUT_FRAME + POST_DRAWS for f in cut.values()), {b: len(f) for b, f in cut.items()}
    cut_specks = {b: specks(f[CUT_FRAME:]) for b, f in cut.items()}
    summary["cut_nose"] = {
        "specks_by_draw": cut_specks,
        "specks_d0_d29": {b: spread(v[:30]) for b, v in cut_specks.items()},
        "specks_d30_d64": {b: spread(v[30:]) for b, v in cut_specks.items()},
    }
    side_by_side(cut, CONE_BOX, 55, CUT_FRAME + POST_DRAWS, PICS / "real-cut-nose-main-branch.mp4",
                 note=lambda b, k: f"d{k - CUT_FRAME} specks {cut_specks[b][k - CUT_FRAME]}" if k >= CUT_FRAME else "")
    strip(cut, NOSE_BOX, [0, 1, 2, 5, 10, 15, 29], PICS / "real-cut-nose-strip-1x.png",
          note=lambda b, d: f"specks {cut_specks[b][d]}")
    print("cut nose", json.dumps(summary["cut_nose"]["specks_d0_d29"]))
    del cut

    cut = pair("cut-lamps")
    assert all(len(f) == CUT_FRAME + POST_DRAWS for f in cut.values()), {b: len(f) for b, f in cut.items()}
    cut_grain = {b: grain(f[CUT_FRAME:], FLOOR_BOX) for b, f in cut.items()}
    summary["cut_lamps"] = {
        "floor_grain_by_draw": cut_grain,
        "floor_grain_ratio_branch_over_main": {f"d{d}": cut_grain["branch"][d] / cut_grain["main"][d]
                                               for d in (0, 2, 5, 10, 15, 25, 29, 45, 64)},
        "flicker_floor_d0_d14": {b: flicker(f[CUT_FRAME:CUT_FRAME + 15], FLOOR_BOX) for b, f in cut.items()},
        "flicker_floor_d15_d29": {b: flicker(f[CUT_FRAME + 15:CUT_FRAME + 30], FLOOR_BOX) for b, f in cut.items()},
        "flicker_floor_d30_d64": {b: flicker(f[CUT_FRAME + 30:], FLOOR_BOX) for b, f in cut.items()},
    }
    side_by_side(cut, FLOOR_BOX, 55, CUT_FRAME + POST_DRAWS, PICS / "real-cut-lamps-main-branch.mp4",
                 note=lambda b, k: f"d{k - CUT_FRAME} grain {cut_grain[b][k - CUT_FRAME]:.4f}" if k >= CUT_FRAME else "")
    strip(cut, FLOOR_STRIP_BOX, [0, 2, 5, 10, 15, 25, 29], PICS / "real-cut-lamps-strip-1x.png",
          note=lambda b, d: f"grain {cut_grain[b][d]:.4f}")
    print("cut lamps", json.dumps(summary["cut_lamps"]["floor_grain_ratio_branch_over_main"]))
    del cut

    # Moving camera: the slow pan and the walk of stage 1's step 2, over the opposite-share surfaces.
    for name, end in (("pan", 300), ("walk", 240)):
        film = pair(name)
        moving_specks = {b: specks(f[60:end]) for b, f in film.items()}
        summary[name] = {
            "frames": {b: len(f) for b, f in film.items()},
            "specks_moving": {b: spread(v) for b, v in moving_specks.items()},
            "flicker_floor_moving": {b: flicker(f[60:end], FLOOR_BOX) for b, f in film.items()},
            "mean_abs_gray_difference_moving": float(np.mean([
                np.abs(luma(a.astype(np.float64)) - luma(c.astype(np.float64))).mean()
                for a, c in zip(film["main"][60:end], film["branch"][60:end])])),
        }
        side_by_side(film, (0, 0, WIDTH, HEIGHT), 0, len(film["main"]), PICS / f"{name}-hall-main-branch.mp4", scale=2)
        print(name, json.dumps(summary[name]["specks_moving"]))
        del film

    # Determinism: two runs of the walk on the same binary give the same frames.
    first, second = framemd5(TAKES / "walk-branch/film.mkv"), framemd5(TAKES / "walk-branch-2/film.mkv")
    summary["determinism_walk"] = {"frames": len(first), "identical": first == second,
                                   "differing_frames": sum(a != b for a, b in zip(first, second)) + abs(
                                       len(first) - len(second)),
                                   "digest": hashlib.sha256("".join(first).encode()).hexdigest()[:16]}
    print("determinism", summary["determinism_walk"])

    (TABLES / "summary.json").write_text(json.dumps(summary, indent=1))


if __name__ == "__main__":
    main()
