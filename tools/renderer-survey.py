#!/usr/bin/env python3
"""Tool code (tools/README.md, "The renderer survey"): measures every scene of scripts/*.json against the goal of epic
ter-8hyd: frame time, specks and blotches in stills, flicker in motion, and the draws until the light settles.

Usage, inside the dev shell from the repository root:
    python3 -I tools/renderer-survey.py probe-build <out>
    python3 -I tools/renderer-survey.py run <out> [--parts timing,still,motion,switch,lag,walk] [--only NAME ...]
    python3 -I tools/renderer-survey.py trace <out> [--slowest 5]
    python3 -I tools/renderer-survey.py report <out>
    python3 -I tools/renderer-survey.py sheet <out>

tools/renderer-survey/cases.json lists the cases. "run" builds the Release demo, writes a script for each case into
<out>/scripts, renders it headless under the GPU lock (tools/gpu-lock.sh), measures the images, deletes them but for
the few that it keeps, and writes one result file per case into <out>/results/<part>/<name>.json. A case whose result
file exists does not run again, so a stopped run goes on where it stopped; delete the file to run the case again.
"report" prints the tables and the verdicts. "sheet" writes <out>/contact-sheet.png. "trace" records a GPU trace of the
slowest views (tools/gpu-trace.sh). "probe-build" builds the lamp switch probe, which the parts switch, lag and walk
need: a copy of HEAD with tools/renderer-survey/probe.patch, in <out>/probe-bin. The probe never enters src.

Every render runs at the budget's configuration: --size 1920x1080, so FSR 3.1 renders at 1280 x 720 (the headless
render scale of 2/3), from the Release build.
"""

import argparse
import json
import math
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CASES = ROOT / "tools" / "renderer-survey" / "cases.json"
PROBE_PATCH = ROOT / "tools" / "renderer-survey" / "probe.patch"
DEMO = ROOT / "src" / "Tertius.Demo" / "bin" / "Release" / "net11.0" / "Tertius.Demo"

# The budget's configuration (docs/plans/rendering-performance.md, decision 1): 1920 x 1080 output, which a headless
# renderer draws at its fixed render scale of 2/3, 1280 x 720, and upscales with FSR 3.1.
SIZE = "1920x1080"
# Render pixels per output pixel on each axis at that scale.
OUTPUT_PER_RENDER = 1.5
TICK_RATE = 60
# A headless capture after at least this many ticks is a still (ARCHITECTURE.md, "The still rule": a step of at least
# 0.09 s), and so is the first drawn frame and a capture right after another capture.
STILL_TICKS = 6
# The draws of a still: the warm-up draws of FSR 3.1 and the screen tracer, 32, and the shown draw.
STILL_DRAWS = 33

# The budget: the GPU frame span at the windowed clock, in ms (ter-odli's note of 2026-11-04).
BUDGET_MS = 12.0
WINDOWED_MHZ = 1296.0
# The frames of a view's timing film, each one tick after the last, so that no frame is a still with warm-up draws.
TIMING_FRAMES = 120

# A still view: captures of the same state, each a still of STILL_DRAWS draws. The shown still is the capture whose
# draws first reach STILL_AT. The reference is the mean of the last REFERENCE_CAPTURES captures, whose first draw comes
# well after the slow history's cap of 32 frames, so each reference capture holds a history independent of the still's.
STILL_AT = 120
STILL_CAPTURES = 28
REFERENCE_CAPTURES = 20

# Display-linear luminance, from 0 to 1, of the 8-bit sRGB captures after the tone curve.
# A pixel is visible when its blurred reference exceeds this: about sRGB code 12 of 255.
VISIBLE = 0.002
# The speck rule of ter-9371: a pixel that exceeds twice the median of its 7 x 7 window and that median by 0.03.
SPECK_WINDOW = 7
SPECK_RATIO = 2.0
SPECK_EXCESS = 0.03
# The blotch score leaves out the pixels within EMITTER_REACH output pixels of a reference pixel above EMITTER: the
# lamps, their bloom and blown-out light. A small far lamp's bloom changes from draw to draw with its sub-pixel place,
# which made rings of error around the lamp hall's far lamps on 2026-11-09, where the light on its floor was quiet.
EMITTER = 0.5
EMITTER_REACH = 40
# A speck of the still counts only where the reference has none within this many output pixels, so that stars, small
# lamps and highlights, which the reference also holds, do not count.
SPECK_REFERENCE_REACH = 2
# The blur of the blotch score and the flicker score: 4 render pixels, in output pixels.
BLUR_SIGMA = 4 * OUTPUT_PER_RENDER
# The floor of the denominator of a relative difference, so that the noise of dark pixels does not dominate.
RELATIVE_FLOOR = 0.01
# The cap of one pixel's relative flicker, so that a moving object, whose pixels change by their whole value, adds at
# most its share of the image.
FLICKER_CAP = 0.5

# A settle: the light stays within this share of its control.
SETTLE_TOLERANCE = 0.05
# The goal of epic ter-8hyd: updates settle within about 60 draws.
SETTLE_GOAL = 60
# The grid of regions whose mean light the settle and lag measures follow, besides the whole image.
GRID = 3
# A region counts for a settle when the change moves its light by more than this share, and for a lag when its light
# changes by more than this share over SETTLE_GOAL draws.
REGION_CHANGE = 0.10

# The verdict thresholds (docs/plans/renderer-finish.md, "Measures and thresholds", gives their reasons).
SPECKS_LIMIT = 1.0
BLOTCH_LIMIT = 0.04
FLICKER_LIMIT = 0.006
GRAIN_FLICKER_LIMIT = 0.05

# Each part of the survey and the list of tools/renderer-survey/cases.json whose cases it runs.
PARTS = {"timing": "stills", "still": "stills", "motion": "motion", "switch": "switches", "lag": "lags", "walk": "walks"}


# ---------------------------------------------------------------------------------------------------------------------
# Scripts


def frame_steps(script: dict):
    """Yields (frame, step, offset) for every rendered frame of the script: a capture with offset None, or the frame of a
    film at that offset in the film. Frames count from 0, as gpu-trace.py trim counts them."""
    frame = 0
    for step in script["steps"]:
        if step["do"] == "capture":
            yield frame, step, None
            frame += 1
        elif step["do"] == "film":
            for offset in range(step["frames"]):
                yield frame + offset, step, offset
            frame += step["frames"]


def frame_count(script: dict) -> int:
    return sum(1 for _ in frame_steps(script))


@dataclass
class Prefix:
    """The steps that bring a script's world to the state of one of its frames without a render."""

    steps: list
    # Ticks since the last drawn frame, or None when no frame was drawn yet.
    ticks_since_draw: int | None


def run_step(ticks: int) -> list:
    return [{"do": "run", "ticks": ticks}] if ticks > 0 else []


def views_script(script: dict, views: dict) -> tuple[dict, dict]:
    """The script with the steps of views[frame] in place of each listed frame, and no other render. The world reaches
    each listed frame's state as in the script: a capture before it is left out, and a film becomes runs of its ticks.
    The script ends after the last listed frame. Returns the script and, for each listed frame, whether its first step
    renders a still: the first drawn frame, or one after at least STILL_TICKS ticks."""
    pending = sorted(views)
    if not pending:
        raise ValueError("no frame listed")
    total = frame_count(script)
    if pending[-1] >= total:
        raise ValueError(f"the script has {total} frames, none numbered {pending[-1]}")
    steps = []
    still = {}
    since = None  # ticks since the last drawn frame; None before the first

    def emit(frame):
        still[frame] = since is None or since >= STILL_TICKS
        steps.extend(views[frame])

    frame = 0
    for step in script["steps"]:
        if not pending:
            break
        kind = step["do"]
        if kind == "capture":
            if frame == pending[0]:
                emit(pending.pop(0))
                since = 0
            frame += 1
        elif kind == "film":
            done = 0
            while pending and pending[0] < frame + step["frames"]:
                offset = pending[0] - frame
                ticks = (offset + 1 - done) * step["every"]
                steps.extend(run_step(ticks))
                if since is not None:
                    since += ticks
                emit(pending.pop(0))
                since = 0
                done = offset + 1
            rest = (step["frames"] - done) * step["every"]
            if pending:
                steps.extend(run_step(rest))
                if since is not None:
                    since += rest
            frame += step["frames"]
        else:
            steps.append(step)
            if kind == "run" and since is not None:
                since += step["ticks"]
    return {**script, "steps": steps}, still


def captures(prefix: str, count: int) -> list:
    return [{"do": "capture", "name": f"{prefix}-s{index:02d}"} for index in range(count)]


def still_capture_index(first_is_still: bool) -> int:
    """The index of the first capture of a view whose draws reach STILL_AT."""
    draws = STILL_DRAWS if first_is_still else 1
    index = 0
    while draws < STILL_AT:
        draws += STILL_DRAWS
        index += 1
    return index


def still_draws(first_is_still: bool, index: int) -> int:
    return (STILL_DRAWS if first_is_still else 1) + STILL_DRAWS * index


def still_script(script: dict, frame: int) -> tuple[dict, bool]:
    """The still view of one frame: the frame's state, then STILL_CAPTURES captures v-s00 ... Returns the script and
    whether its first capture is a still."""
    built, still = views_script(script, {frame: captures("v", STILL_CAPTURES)})
    return built, still[frame]


def timing_script(script: dict, frame: int) -> dict:
    """A view's timing film: the frame's state, then TIMING_FRAMES frames one tick apart."""
    built, _ = views_script(script, {frame: [{"do": "film", "name": "t", "frames": TIMING_FRAMES, "every": 1}]})
    return built


def motion_script(script: dict, start: int, frames: int) -> dict:
    """A film of the script from frame start, as a window draws it: one frame every tick, up to frames frames. A frame of
    a script film that runs N ticks becomes N frames, a capture becomes one frame, and a run of N ticks after frame start
    becomes N frames. So no frame is a still with warm-up draws, which a window never draws. Other steps after frame
    start, such as a camera or a post, keep their place."""
    built, _ = views_script(script, {start: []})
    steps = built["steps"]
    kept = 0

    def film(ticks):
        nonlocal kept
        for _ in range(min(ticks, frames - kept)):
            steps.append({"do": "film", "name": f"m{kept:04d}", "frames": 1, "every": 1})
            kept += 1

    frame = 0
    for step in script["steps"]:
        if kept >= frames:
            break
        kind = step["do"]
        if kind == "capture":
            if frame >= start:
                film(1)
            frame += 1
        elif kind == "film":
            for offset in range(step["frames"]):
                if frame + offset >= start:
                    film(step["every"])
            frame += step["frames"]
        elif frame > start:
            if kind == "run":
                film(step["ticks"])
            else:
                steps.append(step)
    return {**built, "steps": steps}


def scene_script(scene: str, camera: str | None, ticks: int, steps: list) -> dict:
    head = [{"do": "camera", "entity": camera}] if camera else []
    return {"scene": scene, "window": {"width": 1920, "height": 1080}, "steps": head + run_step(ticks) + steps}


# The settle of a lamp switch, as /private/tmp/tertius-diffuse-clamp-20261108/settle2.py measured it on 2026-11-08: a
# still, then a film of SWITCH_FRAMES frames; the selected lamps are off in film frames SWITCH_OFF to SWITCH_ON. The
# lamps-off window is 200 draws: the cabin's light did not settle within the 100 draws of the survey of 2026-11-09, so
# that window gave no count. The lamps-on window stays 100 draws.
SWITCH_OFF = 160
SWITCH_ON = SWITCH_OFF + 200
SWITCH_FRAMES = SWITCH_ON + 100


def switch_script(case: dict) -> dict:
    return scene_script(case["scene"], case["camera"], case["ticks"],
                        [{"do": "capture", "name": "still"}, {"do": "film", "name": "f", "frames": SWITCH_FRAMES, "every": 1}])


def switch_runs(case: dict) -> dict:
    """The three probe runs of a switch case and their environments: the switch, and two controls that hold the lamps
    off or on from the start. A film frame k is draw STILL_DRAWS + k."""
    common = {"TERTIUS_PROBE_LAMP_SEL": case["select"]}
    if case["sun"] == "off":
        common["TERTIUS_PROBE_SUN"] = "off"
    first = STILL_DRAWS + SWITCH_OFF
    last = STILL_DRAWS + SWITCH_ON
    return {
        "switch": {**common, "TERTIUS_PROBE_LAMP_OFF": f"{first}-{last}"},
        "off": {**common, "TERTIUS_PROBE_LAMP_OFF": "0-100000000"},
        "on": common,
    }


def lag_scripts(case: dict) -> tuple[dict, dict]:
    """The film of a lag case, and the stills of the same states: for each sample frame k, the state of film frame k
    and 4 stills, whose last one has 132 draws."""
    film = scene_script(case["scene"], case["camera"], case["ticks"],
                        [{"do": "capture", "name": "still"}, {"do": "film", "name": "f", "frames": case["frames"], "every": 1}])
    steps = []
    done = 0
    for k in case["samples"]:
        steps += run_step(k + 1 - done)
        steps += captures(f"k{k:04d}", 4)
        done = k + 1
    # The film's still comes first in both, so both worlds start alike.
    stills = scene_script(case["scene"], case["camera"], case["ticks"], [{"do": "capture", "name": "still"}] + steps)
    return film, stills


# A walk case's settle compares the film after the stop with a control that stands where the walk ends, frame by frame
# at the same world ticks, as a switch compares with its lamps-off control. So a change of the world's light in the
# window, such as the orbit cabin's sunlit sill strips at tick 303 (ticket ter-g3tk), shows in both and does not count
# as the walk's settle. The control walks right after the head ticks without drawing, which puts the person at the same
# place to 1e-14 m, and then draws the walk's stand and window: the stand gives its own start after the load time to
# settle.


def walk_key(case: dict, kind: str) -> dict:
    return {"do": "post", "to": {"kind": "topic", "name": "input"}, "event": "InputEvent",
            "payload": {"kind": kind, "code": case["key"]}}


def walk_script(case: dict) -> dict:
    return scene_script(case["scene"], case["camera"], case["ticks"], [
        {"do": "capture", "name": "still"},
        {"do": "film", "name": "f", "frames": case["stand"], "every": 1},
        walk_key(case, "keyDown"),
        {"do": "film", "name": "g", "frames": case["walk"], "every": 1},
        walk_key(case, "keyUp"),
        {"do": "film", "name": "h", "frames": case["after"], "every": 1},
    ])


def walk_control_script(case: dict) -> dict:
    return scene_script(case["scene"], case["camera"], case["ticks"], [
        walk_key(case, "keyDown"),
        *run_step(case["walk"]),
        walk_key(case, "keyUp"),
        {"do": "capture", "name": "still"},
        {"do": "film", "name": "c", "frames": case["stand"] + case["after"], "every": 1},
    ])


def walk_control_frame(case: dict, frame: int) -> int:
    """The control's film frame at the world ticks of frame `frame` of the walk's film after the stop."""
    return case["stand"] + frame


def walk_settle(after: list, control: list) -> list:
    """Each region's settle draws: the walk's frames after the stop against the control's frames at the same ticks."""
    return [settle_draws([row[r] for row in after], [row[r] for row in control], 0, len(after))
            for r in range(len(after[0]))]


# ---------------------------------------------------------------------------------------------------------------------
# Measures. NumPy and SciPy load on first use, so that the script functions above need neither.


def np():
    import numpy
    return numpy


def ndimage():
    import scipy.ndimage
    return scipy.ndimage


def luminance(path) -> "numpy.ndarray":
    """The display-linear luminance of an 8-bit sRGB capture, from 0 to 1."""
    from PIL import Image
    n = np()
    a = n.asarray(Image.open(path).convert("RGB"), dtype=n.float32) / 255.0
    a = n.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
    return (0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]).astype(n.float32)


def probe_luminance(path) -> "numpy.ndarray":
    """The scene luminance of a capture of the probe, which writes log2 luminance over 2^-24 to 2^16 in 24 bits."""
    from PIL import Image
    n = np()
    a = n.asarray(Image.open(path).convert("RGB")).astype(n.uint32)
    q = (a[..., 0] << 16) | (a[..., 1] << 8) | a[..., 2]
    y = n.exp2(q / 16777215.0 * 40.0 - 24.0)
    y[q == 0] = 0.0
    return y


def blur(y):
    return ndimage().gaussian_filter(y, BLUR_SIGMA, mode="nearest")


def speck_mask(y):
    """The pixels of ter-9371's speck rule."""
    median = ndimage().median_filter(y, size=SPECK_WINDOW, mode="nearest")
    return (y > SPECK_RATIO * median) & (y - median > SPECK_EXCESS)


def near_emitters(y):
    """The pixels within EMITTER_REACH output pixels of a pixel above EMITTER."""
    size = 2 * EMITTER_REACH + 1
    return ndimage().maximum_filter((y > EMITTER).astype(np().uint8), size=size, mode="constant") > 0


def visible(reference):
    return blur(reference) > VISIBLE


def specks(y, reference) -> float:
    """Specks of the still per 10,000 visible pixels: connected groups of speck pixels with no speck of the reference
    within SPECK_REFERENCE_REACH pixels."""
    nd = ndimage()
    reach = 2 * SPECK_REFERENCE_REACH + 1
    real = nd.binary_dilation(speck_mask(reference), structure=np().ones((reach, reach), dtype=bool))
    mask = visible(reference)
    count = nd.label(speck_mask(y) & ~real & mask)[1]
    return 1e4 * count / max(int(mask.sum()), 1)


def level(y, reference) -> float:
    """The still's light against the reference's over the whole image, as the median ratio of the blurred images over
    the visible pixels, minus 1: a light or an exposure that still changes at the still's draw."""
    n = np()
    mask = visible(reference)
    if not mask.any():
        return 0.0
    # The mask keeps the blurred reference above VISIBLE, so the ratio is finite.
    return float(n.median(blur(y)[mask] / blur(reference)[mask]) - 1)


def blotch(y, reference) -> tuple[float, float]:
    """The low-frequency error of the still against its reference, after the still is divided by its level: the
    magnitude of the relative difference of the two after a blur of BLUR_SIGMA, over the visible pixels away from
    emitters, as its 90th percentile, the blotch score, and its RMS. A change of the whole image's level is no blotch,
    and level() measures it. Both are None when no pixel is visible away from the emitters: nothing was measured."""
    n = np()
    mask = visible(reference) & ~near_emitters(reference)
    if not mask.any():
        return None, None
    b = blur(reference)
    relative = n.abs((blur(y) / (1 + level(y, reference)) - b) / (b + RELATIVE_FLOOR))[mask]
    return float(n.percentile(relative, 90)), float(n.sqrt(n.mean(relative ** 2)))


def flicker(frames) -> tuple[float, float]:
    """The flicker of a film: for each frame t, the second difference y[t] - (y[t-1] + y[t+1]) / 2 relative to the three
    frames' mean, capped at FLICKER_CAP and averaged over the visible pixels away from the emitters of the three frames;
    the median over the frames. A light that changes steadily, a fade or a steady camera pan has a second difference
    near 0. Returns the low-frequency flicker, after a blur of BLUR_SIGMA, and the grain flicker of single pixels, or
    None for both when no frame had a measured pixel. frames is an iterable of luminance images."""
    n = np()
    low = []
    grain = []
    window = []
    for y in frames:
        window.append((y, blur(y)))
        if len(window) < 3:
            continue
        (y0, b0), (y1, b1), (y2, b2) = window[-3:]
        window = window[-2:]
        mean_b = (b0 + b1 + b2) / 3
        mask = (mean_b > VISIBLE) & ~near_emitters(n.maximum(n.maximum(y0, y1), y2))
        if not mask.any():
            continue
        d_low = n.minimum(n.abs(b1 - (b0 + b2) / 2) / (mean_b + RELATIVE_FLOOR), FLICKER_CAP)
        mean_y = (y0 + y1 + y2) / 3
        d_grain = n.minimum(n.abs(y1 - (y0 + y2) / 2) / (mean_y + RELATIVE_FLOOR), FLICKER_CAP)
        low.append(float(d_low[mask].mean()))
        grain.append(float(d_grain[mask].mean()))
    if not low:
        return None, None
    return float(n.median(low)), float(n.median(grain))


def region_means(y) -> list:
    """The mean of the whole image, then of each cell of a GRID x GRID grid, by rows."""
    h, w = y.shape
    out = [float(y.mean())]
    for r in range(GRID):
        for c in range(GRID):
            out.append(float(y[r * h // GRID:(r + 1) * h // GRID, c * w // GRID:(c + 1) * w // GRID].mean()))
    return out


def settle_draws(series, control, first: int, last: int, tolerance: float = SETTLE_TOLERANCE) -> int | None:
    """The draws from frame first until the series stays within tolerance of its control up to frame last: 0 when it
    never leaves, and None when it is still outside at frame last - 1, so that it did not settle within the window of
    last - first draws. settle2.py's rule of 2026-11-08."""
    bad = [k for k in range(first, last) if abs(series[k] - control[k]) > tolerance * abs(control[k])]
    if not bad:
        return 0
    if bad[-1] == last - 1:
        return None
    return bad[-1] - first + 1


def slowest_settle(counts: list) -> int | None:
    """The slowest of several settle counts, such as one per region: None when one of them did not settle within its
    window, and 0 for no count."""
    return None if None in counts else max(counts, default=0)


def settle_text(draws: int | None, window: int) -> str:
    return f"not settled within {window} draws" if draws is None else str(draws)


def changed_regions(off, on) -> list:
    """The regions whose light the switch changes by more than REGION_CHANGE: the indices into region_means."""
    return [r for r in range(len(off)) if on[r] > 0 and abs(off[r] / on[r] - 1) > REGION_CHANGE]


def lag(film, still, slope) -> float | None:
    """How many draws the film's light lags behind the still of the same state, from the film's change per draw: None
    when the light changes too little for a lag to show."""
    if film <= 0 or abs(slope) * SETTLE_GOAL / film <= REGION_CHANGE:
        return None
    return (still - film) / slope


# ---------------------------------------------------------------------------------------------------------------------
# Verdicts


# The verdict of a case that measured nothing, such as a view whose every visible pixel lies near an emitter. It fails,
# so that a case that measures nothing never passes.
NOT_MEASURED = "not measured"


def still_verdict(result: dict) -> list:
    """The measures of a still that fail their threshold."""
    if result["blotch"] is None:
        return [NOT_MEASURED]
    failed = []
    if result["specks"] > SPECKS_LIMIT:
        failed.append("specks")
    if result["blotch"] > BLOTCH_LIMIT:
        failed.append("blotches")
    if abs(result.get("level", 0)) > SETTLE_TOLERANCE:
        failed.append("level")
    return failed


def timing_verdict(result: dict) -> list:
    """A view fails when its p95 at the windowed clock exceeds the budget. With the clock unknown, the raw p95 decides."""
    p95 = result.get("p95AtWindowedMs", result.get("p95Ms"))
    return ["time"] if p95 is not None and p95 > BUDGET_MS else []


def motion_verdict(result: dict) -> list:
    """A film fails on flicker, or on the blotches or the level of its sampled frames against stills of the same states.
    A level within LAG_ERROR_LIMIT is the error that the goal's lag allows a moving light."""
    if result["blotch"] is None or result["flicker"] is None:
        return [NOT_MEASURED]
    failed = []
    if result["blotch"] > BLOTCH_LIMIT:
        failed.append("motion blotches")
    if abs(result.get("level", 0)) > LAG_ERROR_LIMIT:
        failed.append("motion level")
    if result["flicker"] > FLICKER_LIMIT:
        failed.append("flicker")
    if result["grainFlicker"] > GRAIN_FLICKER_LIMIT:
        failed.append("grain flicker")
    return failed


def settle_verdict(draws: int | None) -> list:
    """A settle fails over the goal, and when it did not settle within its window (settle_draws gives None)."""
    return ["settle"] if draws is None or draws > SETTLE_GOAL else []


# A lag case fails when the median magnitude of the film's error against the still of the same state, over the regions
# whose light changes, exceeds this share. A history that lags SETTLE_GOAL draws behind a light that changes by
# REGION_CHANGE in SETTLE_GOAL draws errs by about REGION_CHANGE, so this is the error of the goal's lag at the
# smallest change that counts.
LAG_ERROR_LIMIT = REGION_CHANGE


def lag_error(result: dict) -> float | None:
    """The median magnitude of the film's error over the region samples whose light changes enough for a lag."""
    errors = sorted(abs(row["error"]) for row in result["rows"] if row["lagDraws"] is not None)
    return errors[len(errors) // 2] if errors else None


def lag_verdict(result: dict) -> list:
    """A lag case fails when its median lag exceeds the goal, or on its error. A lag of None says that no light changed
    enough for a lag to show, so the case measured nothing, and it fails."""
    lag_draws = result["lagDraws"]
    if lag_draws is None:
        return [NOT_MEASURED]
    failed = ["lag"] if lag_draws > SETTLE_GOAL else []
    error = lag_error(result)
    if error is not None and error > LAG_ERROR_LIMIT:
        failed.append("lag error")
    return failed


# ---------------------------------------------------------------------------------------------------------------------
# Runs


def load_cases() -> dict:
    return json.loads(CASES.read_text())


def script_of(name: str) -> dict:
    return json.loads((ROOT / "scripts" / name).read_text())


def source_of(case: dict) -> tuple[dict, int]:
    """The script and frame of a case: a frame of a script in scripts/, or a scene, a camera and the ticks before the
    view, for a moment that no script captures."""
    if "script" in case:
        return script_of(case["script"]), case.get("frame", case.get("start", 0))
    built = scene_script(case["scene"], case["camera"], case["ticks"], [{"do": "capture", "name": "view"}])
    return built, 0


def label_of(case: dict) -> str:
    return case.get("script") or f'{case["scene"]} at tick {case["ticks"]}'


def scene_name(case: dict) -> str:
    return case["scene"] if "scene" in case else script_of(case["script"])["scene"]


def free_gb() -> float:
    return shutil.disk_usage("/").free / 1e9


# Set in a chunk of cases that runs under one take of the GPU lock, so that its renders do not take it again.
HOLDS_LOCK = "TERTIUS_SURVEY_HOLDS_LOCK"


def gpu_lock(log: Path, timed: bool) -> list[str]:
    """The command of tools/gpu-lock.sh before the locked command. A timed run holds the GPU alone. Every other run
    renders images for the measures, which do not depend on the time, so it shares the GPU (tools/README.md, "The GPU
    lock")."""
    return ["bash", str(ROOT / "tools" / "gpu-lock.sh"), *([] if timed else ["--shared"]), str(log)]

GPU_SAMPLER = (
    'sleep 1.5; for i in 1 2 3; do ioreg -r -d 1 -c IOAccelerator | grep -o \'"Device Utilization %"=[0-9]*\' | head -1; '
    'sleep 0.5; done; exec "$@"'
)


def render(out: Path, script: dict, *, binary: Path, env: dict | None = None, extra=(), timed=False, clock_bin=None,
           size=SIZE) -> Path:
    """Renders a script headless under the GPU lock into out, at the budget's configuration. A timed run draws no image,
    keeps two frames in flight and runs beside gpu-clock, and its gpu-timings.json gets the clock record."""
    if free_gb() < 5:
        raise SystemExit(f"Only {free_gb():.1f} GB are free on /. Stop before {out}.")
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    path = out / "script.json"
    path.write_text(json.dumps(script, indent=1))
    log = out.parent / f"{out.name}.log"
    command = [str(binary), "--script", str(path), "--headless", "--out", str(out), "--size", size, *extra]
    if timed:
        command = ["bash", str(ROOT / "tools" / "gpu-clock.sh"), *command, "--video", "none", "--in-flight", "2"]
    run_env = {**os.environ, **(env or {})}
    if clock_bin:
        run_env["GPU_CALIBRATION_BIN"] = str(clock_bin)
    sampled = ["bash", "-c", GPU_SAMPLER, "sampler", *command]
    if os.environ.get(HOLDS_LOCK) == "1":
        # A chunk of cases holds the GPU lock already (run_chunks).
        with open(log, "w") as stream:
            code = subprocess.run(sampled, env=run_env, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT).returncode
    else:
        code = subprocess.run([*gpu_lock(log, timed), *sampled], env=run_env, cwd=ROOT).returncode
    text = log.read_text(errors="replace")
    if code != 0:
        raise RuntimeError(f"The run of {path} failed with exit code {code}. The end of {log}:\n" + text[-2000:])
    if "the lock was not this run's at its release" in text:
        raise RuntimeError(f"{log}: the GPU lock was not this run's at its release. Discard the run.")
    if timed:
        subprocess.run([sys.executable, "-I", str(ROOT / "tools" / "gpu_clock.py"), "record", str(log),
                        str(out / "gpu-timings.json")], check=True, capture_output=True)
    return log


def emulators() -> int:
    """The Android emulators that run now. On 2026-11-09 a run beside one raised the p95 of bay-sunbeam from 24.3 to
    33.8 ms at the same clock (docs/plans/renderer-finish.md, decision 9)."""
    found = subprocess.run(["pgrep", "-f", "qemu-system"], capture_output=True, text=True).stdout.split()
    return len(found)


def gpu_busy(log: Path) -> list:
    return [int(v) for v in re.findall(r'"Device Utilization %"=(\d+)', log.read_text(errors="replace"))[:3]]


def save(out: Path, part: str, name: str, result: dict):
    path = out / "results" / part / f"{name}.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(result, indent=1))


def done(out: Path, part: str, name: str) -> bool:
    return (out / "results" / part / f"{name}.json").exists()


def keep_png(src: Path, dst: Path):
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(src, dst)


def run_timing(out: Path, case: dict, args):
    """A view's timing. A timing beside an Android emulator does not count, so the case gets no result file while one
    runs, and a later run times it again."""
    work = out / "work" / "timing" / case["name"]
    before = emulators()
    if before:
        raise RuntimeError(f"{before} Android emulators run. Time {case['name']} again when none runs.")
    log = render(work, timing_script(*source_of(case)), binary=DEMO, timed=True, clock_bin=args.clock_bin)
    if emulators():
        shutil.rmtree(work)
        raise RuntimeError(f"An Android emulator started during the timing of {case['name']}. Time it again.")
    timings = json.loads((work / "gpu-timings.json").read_text())
    clock = timings.get("gpuClock", {})
    frame = timings["frame"]
    result = {"script": label_of(case), "scene": scene_name(case), "frame": case.get("frame", 0),
              "frames": frame["frames"], "medianMs": frame["medianMs"], "p95Ms": frame["p95Ms"], "meanMs": frame["meanMs"],
              "clock": clock, "gpuBusyBefore": gpu_busy(log)}
    if "meanMhz" in clock:
        scale = clock["meanMhz"] / WINDOWED_MHZ
        result["medianAtWindowedMs"] = frame["medianMs"] * scale
        result["p95AtWindowedMs"] = frame["p95Ms"] * scale
    save(out, "timing", case["name"], result)
    shutil.rmtree(work)
    return result


def run_still(out: Path, case: dict, args):
    work = out / "work" / "still" / case["name"]
    script, first_is_still = still_script(*source_of(case))
    index = still_capture_index(first_is_still)
    log = render(work, script, binary=probe_binary(out), env=STILL_CONTINUES, extra=NO_BLOOM)
    n = np()
    shown = luminance(work / f"v-s{index:02d}.png")
    reference = None
    for k in range(STILL_CAPTURES - REFERENCE_CAPTURES, STILL_CAPTURES):
        y = luminance(work / f"v-s{k:02d}.png")
        reference = y if reference is None else reference + y
    reference /= REFERENCE_CAPTURES
    score, rms = blotch(shown, reference)
    result = {"script": label_of(case), "scene": scene_name(case), "frame": case.get("frame", 0),
              "draws": still_draws(first_is_still, index),
              "referenceDraws": [still_draws(first_is_still, STILL_CAPTURES - REFERENCE_CAPTURES),
                                 still_draws(first_is_still, STILL_CAPTURES - 1)],
              "specks": specks(shown, reference), "blotch": score, "blotchRms": rms, "level": level(shown, reference),
              "visibleShare": float(visible(reference).mean()), "meanLuminance": float(reference.mean())}
    result["failed"] = still_verdict(result)
    images = out / "images" / "still"
    keep_png(work / f"v-s{index:02d}.png", images / f"{case['name']}.png")
    from PIL import Image
    srgb = n.where(reference <= 0.0031308, reference * 12.92, 1.055 * n.power(reference, 1 / 2.4) - 0.055)
    Image.fromarray((n.clip(srgb, 0, 1) * 255 + 0.5).astype(n.uint8)).save(images / f"{case['name']}-reference.png")
    save(out, "still", case["name"], result)
    shutil.rmtree(work)
    log.unlink()
    return result


def motion_references_script(motion: dict, frames: list) -> tuple[dict, dict]:
    """Stills of the states of the listed frames of a motion film: for each, the frame's state and 4 stills, whose last
    one has 132 or 133 draws. Returns the script and, for each frame, the name of its reference capture."""
    built, still = views_script(motion, {k: captures(f"r{k:04d}", 4) for k in frames})
    return built, {k: f"r{k:04d}-s{still_capture_index(still[k]):02d}" for k in frames}


@dataclass
class MotionCase:
    """A motion case's film, the frames that it samples, and the script of the stills of their states."""

    film: dict
    # The sampled frames by label: the middle and the last frame.
    samples: dict
    references: dict
    # The reference capture of each sampled frame.
    names: dict


def motion_case(case: dict) -> MotionCase:
    if "script" in case:
        film = motion_script(script_of(case["script"]), case["start"], case["frames"])
    else:
        film = scene_script(case["scene"], case["camera"], case["ticks"],
                            [{"do": "film", "name": "m0000", "frames": case["frames"], "every": 1}])
    samples = {"middle": case["frames"] // 2, "end": case["frames"] - 1}
    references, names = motion_references_script(film, list(samples.values()))
    return MotionCase(film, samples, references, names)


def film_exposure(out: Path, name: str, motion: MotionCase) -> float:
    """The fixed exposure of a film and its stills, in stops: the mean of the auto exposures of the stills of its
    sampled frames. A film that starts at a camera cut adapts its exposure over seconds, while a still adapts at once,
    so the film and its stills must share one fixed exposure, or they differ by their exposure and not by their light."""
    work = out / "work" / "exposure" / name
    render(work, motion.references, binary=probe_binary(out), env=STILL_CONTINUES, extra=NO_BLOOM)
    exposures = {e["capture"]: e["stops"] for e in json.loads((work / "exposure.json").read_text())}
    shutil.rmtree(work)
    return sum(exposures[motion.names[k]] for k in motion.samples.values()) / len(motion.samples)


def run_motion(out: Path, case: dict, args):
    """A film and stills of the states of its middle and last frames, all at one fixed exposure (film_exposure)."""
    motion = motion_case(case)
    stops = film_exposure(out, case["name"], motion)
    fixed = {"TERTIUS_PROBE_EXPOSURE": f"{stops:.4f}"}
    reference_work = out / "work" / "motion" / f"{case['name']}-references"
    render(reference_work, motion.references, binary=probe_binary(out), env={**STILL_CONTINUES, **fixed}, extra=NO_BLOOM)
    work = out / "work" / "motion" / case["name"]
    render(work, motion.film, binary=probe_binary(out), env=fixed, extra=NO_BLOOM)
    files = sorted(work.glob("m*-*.png"))
    low, grain = flicker(luminance(f) for f in files)
    images = out / "images" / "motion"
    for label, k in (("start", 0), *motion.samples.items()):
        keep_png(files[k], images / f"{case['name']}-{label}.png")
    # Each sampled frame against a still of the same state: the blotches and the level that motion adds.
    against = {}
    for label, k in motion.samples.items():
        y, r = luminance(files[k]), luminance(reference_work / f"{motion.names[k]}.png")
        score, rms = blotch(y, r)
        against[label] = {"blotch": score, "blotchRms": rms, "level": level(y, r)}
        keep_png(reference_work / f"{motion.names[k]}.png", images / f"{case['name']}-{label}-still.png")
    shutil.rmtree(reference_work)
    scores = [a["blotch"] for a in against.values()]
    result = {"script": label_of(case), "scene": scene_name(case), "start": case.get("start", case.get("ticks")),
              "frames": len(files), "view": case["view"], "stops": stops, "flicker": low, "grainFlicker": grain,
              "blotch": None if None in scores else max(scores),
              "blotchRms": None if None in scores else max(a["blotchRms"] for a in against.values()),
              "level": min((a["level"] for a in against.values()), key=lambda v: -abs(v)), "samples": against}
    result["failed"] = motion_verdict(result)
    save(out, "motion", case["name"], result)
    shutil.rmtree(work)
    return result


def probe_binary(out: Path) -> Path:
    binary = out / "probe-bin" / "Tertius.Demo"
    if not binary.exists():
        raise SystemExit(f"{binary} is missing. Build it first: python3 -I tools/renderer-survey.py probe-build {out}")
    return binary


PROBE_OPTIONS = ("--bloom", "off", "--tone-curve", "hable")
# The renderer resets its history at the first draw of every still (ARCHITECTURE.md, "The still rule"), so each capture
# of an unchanged state would be the same fresh still of 33 draws. With this setting, the probe keeps the history
# through the stills of an unchanged state, as a window keeps drawing an unchanged view, and its random numbers go on.
STILL_CONTINUES = {"TERTIUS_PROBE_STILL_CONTINUES": "1"}
# The still and motion parts measure the light, so they render without bloom: a bright glow's bloom changes from draw to
# draw with its sub-pixel place, which made a ring of error around the glow of orbit-umbra on 2026-11-09.
NO_BLOOM = ("--bloom", "off")


# The probe's series file in a run's folder: one line per capture, its name and then its region_means, which the probe
# computes from its readback as probe_luminance and region_means do. A film of every draw then writes no PNG: a 460-frame
# switch run spent about 1.2 s per frame on PNGs on the cloud's GPU worker, which draws a frame in about 9 ms (ticket
# ter-d7qy). The probe writes a PNG only for the names in TERTIUS_PROBE_KEEP.
SERIES_FILE = "series.txt"


def series_env(work: Path, keep=()) -> dict:
    """The probe's environment for a run into work that measures every capture and writes the PNGs of keep alone."""
    return {"TERTIUS_PROBE_SERIES": str(work / SERIES_FILE), "TERTIUS_PROBE_KEEP": ",".join(keep)}


def film_frame(film: str, k: int) -> str:
    """The capture name of frame k of a film, as FilmStep.FrameName gives it."""
    return f"{film}-{k:06d}"


def probe_rows(work: Path) -> dict:
    """The region means of each capture of a run into work, by capture name."""
    rows = {}
    for line in (work / SERIES_FILE).read_text().splitlines():
        name, *values = line.split()
        rows[name] = [float(v) for v in values]
    return rows


def probe_series(work: Path, film: str) -> list:
    """The region means of each frame of the named film, in frame order."""
    rows = probe_rows(work)
    names = sorted(name for name in rows if name.startswith(film + "-") and name[len(film) + 1:].isdigit())
    return [rows[name] for name in names]


# The film frames of a switch whose images the report shows: the last frames before the lamps go off and come back on,
# and the eighth after each.
SWITCH_KEPT = (SWITCH_OFF - 1, SWITCH_OFF + 8, SWITCH_ON - 1, SWITCH_ON + 8)


def run_switch(out: Path, case: dict, args):
    binary = probe_binary(out)
    series = {}
    for label, env in switch_runs(case).items():
        work = out / "work" / "switch" / f"{case['name']}-{label}"
        kept = SWITCH_KEPT if label == "switch" else ()
        render(work, switch_script(case), binary=binary,
               env={"TERTIUS_PROBE_EXPOSURE": args.exposure, **env,
                    **series_env(work, [film_frame("f", k) for k in kept])},
               extra=PROBE_OPTIONS)
        series[label] = probe_series(work, "f")
        for k in kept:
            keep_png(work / f"{film_frame('f', k)}.png", out / "images" / "switch" / f"{case['name']}-{k:03d}.png")
        shutil.rmtree(work)
    result = switch_result(case, series)
    save(out, "switch", case["name"], result)
    return result


def switch_result(case: dict, series: dict) -> dict:
    """The settle counts of a switch case from the region means of its three runs, by run label."""
    off_draws, on_draws = [], []
    regions = changed_regions(series["off"][SWITCH_ON - 1], series["on"][SWITCH_ON - 1])
    for r in regions:
        m = [row[r] for row in series["switch"]]
        off_draws.append(settle_draws(m, [row[r] for row in series["off"]], SWITCH_OFF, SWITCH_ON))
        on_draws.append(settle_draws(m, [row[r] for row in series["on"]], SWITCH_ON, SWITCH_FRAMES))
    whole = [row[0] for row in series["switch"]]
    result = {"scene": case["scene"], "regions": regions,
              "offDraws": slowest_settle(off_draws), "onDraws": slowest_settle(on_draws),
              "offWindow": SWITCH_ON - SWITCH_OFF, "onWindow": SWITCH_FRAMES - SWITCH_ON,
              "offDrawsWhole": settle_draws(whole, [row[0] for row in series["off"]], SWITCH_OFF, SWITCH_ON),
              "onDrawsWhole": settle_draws(whole, [row[0] for row in series["on"]], SWITCH_ON, SWITCH_FRAMES),
              "change": series["off"][SWITCH_ON - 1][0] / series["on"][SWITCH_ON - 1][0] - 1,
              "series": series}
    # A switch that changes no region by more than REGION_CHANGE measured nothing.
    result["failed"] = [NOT_MEASURED] if not regions else sorted(
        set(settle_verdict(result["offDraws"]) + settle_verdict(result["onDraws"])))
    return result


def run_lag(out: Path, case: dict, args):
    binary = probe_binary(out)
    film_script, stills_script = lag_scripts(case)
    env = {"TERTIUS_PROBE_EXPOSURE": args.exposure}
    work = out / "work" / "lag" / f"{case['name']}-film"
    render(work, film_script, binary=binary, env={**env, **series_env(work)}, extra=PROBE_OPTIONS)
    film = probe_series(work, "f")
    shutil.rmtree(work)
    work = out / "work" / "lag" / f"{case['name']}-stills"
    render(work, stills_script, binary=binary, env={**env, **STILL_CONTINUES, **series_env(work)}, extra=PROBE_OPTIONS)
    measured = probe_rows(work)
    stills = {k: measured[f"k{k:04d}-s03"] for k in case["samples"]}
    shutil.rmtree(work)
    rows = []
    for k in case["samples"]:
        for r in range(len(film[k])):
            slope = (film[min(k + 5, len(film) - 1)][r] - film[k - 5][r]) / (min(k + 5, len(film) - 1) - (k - 5))
            rows.append({"frame": k, "region": r, "film": film[k][r], "still": stills[k][r],
                         "error": film[k][r] / stills[k][r] - 1 if stills[k][r] > 0 else None,
                         "lagDraws": lag(film[k][r], stills[k][r], slope)})
    lags = [row["lagDraws"] for row in rows if row["lagDraws"] is not None]
    errors = [abs(row["error"]) for row in rows if row["error"] is not None]
    n = np()
    result = {"scene": case["scene"], "rows": rows,
              "lagDraws": float(n.median(lags)) if lags else None,
              "lagDrawsMax": float(max(lags)) if lags else None,
              "errorMax": float(max(errors)) if errors else None}
    result["errorMedian"] = lag_error(result)
    result["failed"] = lag_verdict(result)
    save(out, "lag", case["name"], result)
    return result


def run_walk(out: Path, case: dict, args):
    binary = probe_binary(out)
    work = out / "work" / "walk" / f"{case['name']}-walk"
    env = {"TERTIUS_PROBE_EXPOSURE": args.exposure}
    render(work, walk_script(case), binary=binary, env={**env, **series_env(work)}, extra=PROBE_OPTIONS)
    after = probe_series(work, "h")
    during = probe_series(work, "g")
    shutil.rmtree(work)
    work = out / "work" / "walk" / f"{case['name']}-control"
    render(work, walk_control_script(case), binary=binary, env={**env, **series_env(work)}, extra=PROBE_OPTIONS)
    film = probe_series(work, "c")
    control = [film[walk_control_frame(case, k)] for k in range(len(after))]
    shutil.rmtree(work)
    draws = walk_settle(after, control)
    result = {"scene": case["scene"], "settleDraws": slowest_settle(draws), "settleDrawsWhole": draws[0],
              "regionDraws": draws, "window": len(after), "after": after, "during": during, "control": control}
    result["failed"] = settle_verdict(result["settleDraws"])
    save(out, "walk", case["name"], result)
    return result


def run_chunks(args) -> int:
    """Runs the pending cases in chunks of args.chunk, each under one take of the GPU lock, so that a chunk waits once
    for the other users of the lock and not once per render."""
    out = Path(args.out).resolve()
    cases = load_cases()
    if not args.no_build:
        subprocess.run(["dotnet", "build", "src/Tertius.Demo", "-c", "Release", "-v", "quiet", "-nologo"], cwd=ROOT,
                       check=True, stdout=subprocess.DEVNULL)
    code = 0
    for part in args.parts.split(","):
        pending = [c["name"] for c in cases[PARTS[part]] if (not args.only or c["name"] in args.only)
                   and not done(out, part, c["name"])]
        for first in range(0, len(pending), args.chunk):
            chunk = pending[first:first + args.chunk]
            log = out / "logs" / f"{part}-{chunk[0]}.lock.log"
            log.parent.mkdir(parents=True, exist_ok=True)
            command = [sys.executable, "-I", str(Path(__file__).resolve()), "run", str(out), "--parts", part, "--no-build",
                       "--chunk", "0", "--exposure", args.exposure, "--only", *chunk]
            if args.clock_bin:
                command += ["--clock-bin", str(args.clock_bin)]
            result = subprocess.run([*gpu_lock(log, part == "timing"), "env", f"{HOLDS_LOCK}=1", *command],
                                    cwd=ROOT).returncode
            print(log.read_text(errors="replace"), flush=True)
            code = code or result
    return code


def run(args):
    # A caller that holds the GPU lock already renders in this process. A chunk would queue behind the caller and stall
    # the queue (t1-1zic).
    if args.chunk > 0 and os.environ.get(HOLDS_LOCK) != "1":
        return run_chunks(args)
    out = Path(args.out).resolve()
    cases = load_cases()
    parts = args.parts.split(",")
    if not args.no_build:
        subprocess.run(["dotnet", "build", "src/Tertius.Demo", "-c", "Release", "-v", "quiet", "-nologo"], cwd=ROOT,
                       check=True, stdout=subprocess.DEVNULL)
    runners = {"timing": run_timing, "still": run_still, "motion": run_motion, "switch": run_switch, "lag": run_lag,
               "walk": run_walk}
    failures = 0
    for part in parts:
        runner = runners[part]
        for case in cases[PARTS[part]]:
            if args.only and case["name"] not in args.only:
                continue
            if done(out, part, case["name"]):
                continue
            print(f"{part} {case['name']} ...", flush=True)
            try:
                result = runner(out, case, args)
                print(f"{part} {case['name']}: " + json.dumps({k: v for k, v in result.items()
                                                                if not isinstance(v, (list, dict)) or k == "failed"}),
                      flush=True)
            except (RuntimeError, ValueError, OSError) as error:
                failures += 1
                print(f"{part} {case['name']} FAILED: {error}", flush=True)
    return 1 if failures else 0


# ---------------------------------------------------------------------------------------------------------------------
# The split by light signal: which signal carries a failure of a film or a still


# The probe's renders of one light signal alone (TERTIUS_PROBE_SIGNAL of probe.patch), and of the whole image. Every pass
# after the composite, such as glass, particles and haze, adds its light to each of the four renders.
SIGNALS = {"all": "0", "direct": "1", "bounce": "2", "reflections": "3"}
# The parts of the split: the three signals, and "after", the light of the passes after the composite.
PARTS_OF_LIGHT = ("direct", "bounce", "reflections", "after")
# The split keeps each image blurred by BLUR_SIGMA and reduced by this factor on each axis, which the blur allows.
SPLIT_REDUCE = 3


def display(y):
    """The display-linear luminance of a pre-exposed scene luminance under the default tone curve, the ACES fit of
    tonemap.glsl taken on the luminance alone. The split's masks and its floor come from it."""
    n = np()
    fitted = (y * (y + 0.0245786) - 0.000090537) / (y * (0.983729 * y + 0.4329510) + 0.238081)
    return n.clip(fitted, 0.0, 1.0)


# The scene luminance whose display() is RELATIVE_FLOOR: the split's floor of the denominator.
SCENE_FLOOR = 0.0408


def reduce(y):
    """y blurred by BLUR_SIGMA and reduced by SPLIT_REDUCE."""
    return blur(y)[::SPLIT_REDUCE, ::SPLIT_REDUCE].astype(np().float32)


def split_mask(still):
    """The pixels that the blotch score of the still counts (blotch()): visible and away from emitters, judged on the
    display() of the scene luminance of the whole image, reduced as reduce() reduces."""
    d = display(still)
    return (visible(d) & ~near_emitters(d))[::SPLIT_REDUCE, ::SPLIT_REDUCE]


def parts_of(images: dict) -> dict:
    """The parts of the light from the four renders of one moment, each reduced: the passes after the composite add
    their light to all four, so it is (direct + bounce + reflections - all) / 2, and each signal's own light is its
    render less that."""
    after = (images["direct"] + images["bounce"] + images["reflections"] - images["all"]) / 2
    parts = {name: images[name] - after for name in ("direct", "bounce", "reflections")}
    parts["after"] = after
    return parts


def signal_errors(film: dict, still: dict, mask) -> dict:
    """The blotch of a film frame against the still of the same state, split by the part of the light that carries it.
    film and still map each render of SIGNALS to its reduced scene luminance. The whole image's error is blotch()'s, in
    scene luminance: the relative difference after the film is divided by its level, with the floor SCENE_FLOOR, as its
    90th percentile over the mask. Each part's level is its film's light over its still's, less 1, and its blotch is
    the same measure of its own difference after its film is divided by its own level, still relative to the whole
    image's light. So a part that only gets brighter or darker shows in its level and not as a blotch of another part.
    Each part also gets its share of the still's light."""
    n = np()
    total = still["all"]
    if not mask.any():
        return {"blotch": None, "level": None, "parts": {}}
    whole_level = float(n.median(film["all"][mask] / n.maximum(total[mask], 1e-12)) - 1)
    denominator = total + SCENE_FLOOR

    def score(difference):
        return float(n.percentile(n.abs(difference / denominator)[mask], 90))

    film_parts, still_parts = parts_of(film), parts_of(still)
    light = float(total[mask].sum())
    parts = {}
    for name in PARTS_OF_LIGHT:
        f, s = float(film_parts[name][mask].sum()), float(still_parts[name][mask].sum())
        level_of_part = f / s - 1 if abs(s) > 1e-3 * light and f / s > 0 else None
        parts[name] = {"blotch": score(film_parts[name] / (1 + (level_of_part or 0.0)) - still_parts[name]),
                       "level": level_of_part, "share": s / light if light > 0 else 0.0}
    return {"blotch": score(film["all"] / (1 + whole_level) - total), "level": whole_level, "parts": parts}


def signal_flicker(frames) -> dict:
    """The low-frequency flicker of a film by part of the light: flicker()'s second difference of each part, relative
    to the mean of the whole image's three frames, capped at FLICKER_CAP and averaged over the visible reduced pixels,
    then the median over the frames. frames is an iterable of dicts that map each render of SIGNALS to its reduced
    image. "all" is the whole image's flicker in the same measure."""
    n = np()
    series = {name: [] for name in ("all", *PARTS_OF_LIGHT)}
    window = []
    for images in frames:
        window.append({"all": images["all"], **parts_of(images)})
        if len(window) < 3:
            continue
        p0, p1, p2 = window[-3:]
        window = window[-2:]
        mean = (p0["all"] + p1["all"] + p2["all"]) / 3
        mask = display(mean) > VISIBLE
        if not mask.any():
            continue
        for name in series:
            d = n.minimum(n.abs(p1[name] - (p0[name] + p2[name]) / 2) / (mean + SCENE_FLOOR), FLICKER_CAP)
            series[name].append(float(d[mask].mean()))
    return {name: float(n.median(values)) if values else None for name, values in series.items()}


# A native split renders at the render size of the budget, 1280 x 720, without FSR 3.1 (--upscaling off), and scales
# each image up to the output size, so that its measures match the split with FSR. It tells FSR's own history apart
# from the renderer's signals.
NATIVE_SIZE = "1280x720"


def signal_render(out: Path, name: str, script: dict, signal: str, stops: float, *, still=False, native=False) -> Path:
    env = {"TERTIUS_PROBE_SIGNAL": SIGNALS[signal], "TERTIUS_PROBE_EXPOSURE": f"{stops:.4f}"}
    if still:
        env.update(STILL_CONTINUES)
    work = out / "work" / "split" / f"{name}-{signal}"
    if native:
        render(work, script, binary=probe_binary(out), env=env, extra=(*PROBE_OPTIONS, "--upscaling", "off"),
               size=NATIVE_SIZE)
    else:
        render(work, script, binary=probe_binary(out), env=env, extra=PROBE_OPTIONS)
    return work


def signal_luminance(path: Path, native: bool):
    """The scene luminance of a capture of the split, at the output size."""
    y = probe_luminance(path)
    return ndimage().zoom(y, OUTPUT_PER_RENDER, order=1) if native else y


def split_motion(out: Path, case: dict, stops: float | None, native=False) -> dict:
    """A film's split: for each render of SIGNALS, the film and the stills of its middle and last frames, at the fixed
    exposure stops, in scene luminance. Without stops, the stills render first with the auto exposure, and the film
    takes the mean of their exposures, as run_motion does."""
    motion = motion_case(case)
    script, samples, references, names = motion.film, motion.samples, motion.references, motion.names
    if stops is None:
        stops = film_exposure(out, case["name"], motion)
    films, stills, masks = {}, {}, {}
    for signal in SIGNALS:
        work = signal_render(out, f"{case['name']}-references", references, signal, stops, still=True, native=native)
        for label, k in samples.items():
            y = signal_luminance(work / f"{names[k]}.png", native)
            stills.setdefault(label, {})[signal] = reduce(y)
            if signal == "all":
                masks[label] = split_mask(y)
        shutil.rmtree(work)
        work = signal_render(out, case["name"], script, signal, stops, native=native)
        films[signal] = [reduce(signal_luminance(f, native)) for f in sorted(work.glob("m*-*.png"))]
        shutil.rmtree(work)
    frames = len(films["all"])
    flicker_parts = signal_flicker({signal: films[signal][t] for signal in SIGNALS} for t in range(frames))
    against = {label: signal_errors({signal: films[signal][k] for signal in SIGNALS}, stills[label], masks[label])
               for label, k in samples.items()}
    return {"kind": "motion", "native": native, "stops": stops, "frames": frames, "flicker": flicker_parts,
            "samples": against}


def split_still(out: Path, case: dict) -> dict:
    """A still's split: the auto exposure of the shown still first, then for each render of SIGNALS the still view at
    that fixed exposure, in scene luminance: the shown still against the mean of the reference captures."""
    script, first_is_still = still_script(*source_of(case))
    index = still_capture_index(first_is_still)
    work = out / "work" / "split" / f"{case['name']}-exposure"
    render(work, views_script(source_of(case)[0], {source_of(case)[1]: captures("v", index + 1)})[0],
           binary=probe_binary(out), env=STILL_CONTINUES, extra=NO_BLOOM)
    stops = {e["capture"]: e["stops"] for e in json.loads((work / "exposure.json").read_text())}[f"v-s{index:02d}"]
    shutil.rmtree(work)
    shown, reference, mask = {}, {}, None
    for signal in SIGNALS:
        work = signal_render(out, case["name"], script, signal, stops, still=True)
        y = probe_luminance(work / f"v-s{index:02d}.png")
        shown[signal] = reduce(y)
        total = sum(probe_luminance(work / f"v-s{k:02d}.png") for k in range(STILL_CAPTURES - REFERENCE_CAPTURES, STILL_CAPTURES))
        reference[signal] = reduce(total / REFERENCE_CAPTURES)
        if signal == "all":
            mask = split_mask(total / REFERENCE_CAPTURES)
        shutil.rmtree(work)
    return {"kind": "still", "stops": stops, "draws": still_draws(first_is_still, index),
            "samples": {"shown": signal_errors(shown, reference, mask)}}


def split(args):
    """Splits the named films and stills by light signal into <out>/results/split/<name>.json. A film takes the fixed
    exposure of its result in the survey folder args.survey when that exists. Otherwise it renders its own auto
    exposure first, as a still always does. --native applies to films only."""
    if args.native and args.stills:
        raise SystemExit("--native renders films only. Leave out --stills.")
    out = Path(args.out).resolve()
    cases = load_cases()
    motion = {c["name"]: c for c in cases["motion"]}
    stills = {c["name"]: c for c in cases["stills"]}
    failures = 0
    for kind, names in (("motion", args.films or []), ("still", args.stills or [])):
        for name in names:
            key = f"{kind}-{name}" + ("-native" if args.native else "")
            if done(out, "split", key):
                continue
            print(f"split {key} ...", flush=True)
            try:
                if kind == "motion":
                    survey = Path(args.survey or out) / "results" / "motion" / f"{name}.json"
                    stops = json.loads(survey.read_text())["stops"] if survey.exists() else None
                    result = split_motion(out, motion[name], stops, native=args.native)
                else:
                    result = split_still(out, stills[name])
            except (RuntimeError, ValueError, OSError, KeyError) as error:
                failures += 1
                print(f"split {key} FAILED: {error!r}", flush=True)
                continue
            save(out, "split", key, result)
            print(f"split {key}: " + json.dumps(result["samples"]), flush=True)
    return 1 if failures else 0


def split_report(args):
    """The tables of the split, in Markdown: for each sample, the whole image's blotch and level, and each part's blotch,
    level and share of the light. A film adds its flicker by part."""
    out = Path(args.out).resolve()
    lines = ["| Case | Sample | Whole blotch | Whole level | " + " | ".join(f"{p} blotch / level / share" for p in PARTS_OF_LIGHT)
             + " |", "|---|---|---:|---:|" + "---|" * len(PARTS_OF_LIGHT)]
    for name, r in results(out, "split").items():
        for label, sample in r["samples"].items():
            parts = sample["parts"]
            lines.append(f"| {name} | {label} | {cell(sample['blotch'], 3)} | {cell(sample['level'], 3)} | " + " | ".join(
                f"{cell(parts[p]['blotch'], 3)} / {cell(parts[p]['level'], 3)} / {cell(parts[p]['share'], 2)}"
                for p in PARTS_OF_LIGHT) + " |")
    lines += ["", "| Film | Whole flicker | " + " | ".join(f"{p} flicker" for p in PARTS_OF_LIGHT) + " |",
              "|---|---:|" + "---:|" * len(PARTS_OF_LIGHT)]
    for name, r in results(out, "split").items():
        if r["kind"] == "motion":
            lines.append(f"| {name} | {cell(r['flicker']['all'], 4)} | "
                         + " | ".join(cell(r["flicker"][p], 4) for p in PARTS_OF_LIGHT) + " |")
    print("\n".join(lines))
    return 0


# ---------------------------------------------------------------------------------------------------------------------
# Probe build, trace, report and contact sheet


def probe_build(args):
    out = Path(args.out).resolve()
    source = out / "probe-root"
    if source.exists():
        shutil.rmtree(source)
    source.mkdir(parents=True)
    archive = subprocess.run(["git", "archive", "HEAD"], cwd=ROOT, check=True, capture_output=True).stdout
    subprocess.run(["tar", "-x", "-C", str(source)], input=archive, check=True)
    subprocess.run(["patch", "-p1", "--forward", "-i", str(PROBE_PATCH)], cwd=source, check=True)
    subprocess.run(["dotnet", "build", "src/Tertius.Demo", "-c", "Release", "-v", "quiet", "-nologo",
                    "-p:RunAnalyzers=false", "-p:TreatWarningsAsErrors=false", "-o", str(out / "probe-bin")],
                   cwd=source, check=True)
    shutil.rmtree(source)
    print(out / "probe-bin" / "Tertius.Demo")
    return 0


def results(out: Path, part: str) -> dict:
    folder = out / "results" / part
    return {p.stem: json.loads(p.read_text()) for p in sorted(folder.glob("*.json"))} if folder.exists() else {}


def trace(args):
    out = Path(args.out).resolve()
    timing = results(out, "timing")
    slowest = sorted(timing, key=lambda n: -timing[n].get("medianAtWindowedMs", timing[n]["medianMs"]))[:args.slowest]
    slowest += [name for name in args.also or [] if name not in slowest]
    cases = {c["name"]: c for c in load_cases()["stills"]}
    for name in slowest:
        folder = out / "trace" / name
        if (folder / "passes.md").exists():
            continue
        if folder.exists():
            shutil.rmtree(folder)
        folder.parent.mkdir(parents=True, exist_ok=True)
        script = out / "trace" / f"{name}.json"
        script.write_text(json.dumps(timing_script(*source_of(cases[name])), indent=1))
        env = {**os.environ}
        if args.clock_bin:
            env["GPU_CALIBRATION_BIN"] = str(args.clock_bin)
        subprocess.run(["bash", str(ROOT / "tools" / "gpu-trace.sh"), "--no-counters", "--start", "0", "--frames",
                        str(TIMING_FRAMES), "--size", SIZE, "--out", str(folder), str(script), "--in-flight", "2"],
                       cwd=ROOT, env=env, check=True)
    return 0


def cell(value, digits=2):
    if value is None:
        return "n/a"
    if isinstance(value, float):
        return f"{value:.{digits}f}"
    return str(value)


def report(args):
    out = Path(args.out).resolve()
    cases = load_cases()
    timing, still, motion = results(out, "timing"), results(out, "still"), results(out, "motion")
    lines = ["| View | Script | Median ms | p95 ms | Clock MHz | Specks | Blotch | Level | Verdict |",
             "|---|---|---:|---:|---:|---:|---:|---:|---|"]
    for case in cases["stills"]:
        name = case["name"]
        t, s = timing.get(name, {}), still.get(name, {})
        failed = (timing_verdict(t) if t else []) + (still_verdict(s) if s else [])
        verdict = "not run" if not t and not s else ("fail: " + ", ".join(failed) if failed else "pass")
        lines.append(f"| {name} | {label_of(case)} | {cell(t.get('medianAtWindowedMs'))} | {cell(t.get('p95AtWindowedMs'))} "
                     f"| {cell(t.get('clock', {}).get('meanMhz'), 0)} | {cell(s.get('specks'))} | {cell(s.get('blotch'), 3)} "
                     f"| {cell(s.get('level'), 3)} | {verdict} |")
    lines += ["", "| Film | Script | Camera | Flicker | Grain flicker | Blotch | Level | Verdict |",
              "|---|---|---|---:|---:|---:|---:|---|"]
    for name, m in motion.items():
        failed = motion_verdict(m)
        lines.append(f"| {name} | {m['script']} | {m['view']} | {cell(m['flicker'], 4)} | {cell(m['grainFlicker'], 3)} "
                     f"| {cell(m.get('blotch'), 3)} | {cell(m.get('level'), 3)} "
                     f"| {'fail: ' + ', '.join(failed) if failed else 'pass'} |")
    lines += ["", "| Change | Kind | Settle draws | Whole image | Verdict |", "|---|---|---:|---:|---|"]
    for name, r in results(out, "switch").items():
        for edge in ("off", "on"):
            # A result from before 2026-11-10 has no window, and its counts are never None.
            draws, window = r[f"{edge}Draws"], r.get(f"{edge}Window")
            lines.append(f"| {name} lamps {edge} | switch | {settle_text(draws, window)} "
                         f"| {settle_text(r[f'{edge}DrawsWhole'], window)} | {'fail' if settle_verdict(draws) else 'pass'} |")
    for name, r in results(out, "lag").items():
        error = lag_error(r)
        failed = lag_verdict(r)
        lines.append(f"| {name} | lag | {cell(r['lagDraws'], 0)} (max {cell(r['lagDrawsMax'], 0)}) "
                     f"| error {cell(100 * error if error is not None else None, 1)} % "
                     f"| {'fail: ' + ', '.join(failed) if failed else 'pass'} |")
    for name, r in results(out, "walk").items():
        window = r.get("window", len(r["after"]))
        lines.append(f"| {name} | walk stop | {settle_text(r['settleDraws'], window)} "
                     f"| {settle_text(r['settleDrawsWhole'], window)} | {'fail' if settle_verdict(r['settleDraws']) else 'pass'} |")
    print("\n".join(lines))
    return 0


def sheet(args):
    from PIL import Image, ImageDraw, ImageFont
    out = Path(args.out).resolve()
    names = [c["name"] for c in load_cases()["stills"] if (out / "images" / "still" / f"{c['name']}.png").exists()]
    if not names:
        raise SystemExit(f"No still image in {out / 'images' / 'still'}.")
    still = results(out, "still")
    timing = results(out, "timing")
    width, height, label = 384, 216, 34
    columns = 8
    rows = math.ceil(len(names) / columns)
    canvas = Image.new("RGB", (columns * width, rows * (height + label)), (16, 16, 16))
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default(size=15)
    for i, name in enumerate(names):
        x, y = (i % columns) * width, (i // columns) * (height + label)
        canvas.paste(Image.open(out / "images" / "still" / f"{name}.png").convert("RGB").resize((width, height)), (x, y))
        s, t = still.get(name, {}), timing.get(name, {})
        failed = (still_verdict(s) if s else []) + (timing_verdict(t) if t else [])
        color = (235, 90, 80) if failed else (120, 220, 120)
        draw.text((x + 4, y + height + 1), name, fill=(235, 235, 235), font=font)
        draw.text((x + 4, y + height + 17), f"{cell(t.get('p95AtWindowedMs'), 1)} ms  sp {cell(s.get('specks'), 1)}  "
                  f"bl {cell(s.get('blotch'), 3)}  " + ("fail" if failed else "pass"), fill=color, font=font)
    path = out / "contact-sheet.png"
    canvas.save(path)
    print(path)
    return 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("probe-build")
    p.add_argument("out")
    p = sub.add_parser("run")
    p.add_argument("out")
    p.add_argument("--parts", default=",".join(PARTS))
    p.add_argument("--only", nargs="*")
    p.add_argument("--no-build", action="store_true")
    p.add_argument("--chunk", type=int, default=8,
                   help="cases per take of the GPU lock; 0 runs in this process, each render taking the lock itself")
    p.add_argument("--exposure", default="4.08", help="the probe's fixed exposure in stops")
    p.add_argument("--clock-bin", default=os.environ.get("GPU_CALIBRATION_BIN"))
    p = sub.add_parser("trace")
    p.add_argument("out")
    p.add_argument("--slowest", type=int, default=5, help="how many views, slowest by median frame span")
    p.add_argument("--also", nargs="*", help="more views to trace, by name")
    p.add_argument("--clock-bin", default=os.environ.get("GPU_CALIBRATION_BIN"))
    p = sub.add_parser("split")
    p.add_argument("out")
    p.add_argument("--survey", help="a survey folder whose motion results give the films' exposure")
    p.add_argument("--films", nargs="*", help="motion cases to split, by name")
    p.add_argument("--stills", nargs="*", help="still cases to split, by name")
    p.add_argument("--native", action="store_true", help="films only: render without FSR 3.1 at the render size")
    for name in ("report", "sheet", "split-report"):
        sub.add_parser(name).add_argument("out")
    args = parser.parse_args(argv)
    return {"probe-build": probe_build, "run": run, "trace": trace, "report": report, "sheet": sheet, "split": split,
            "split-report": split_report}[args.command](args)


if __name__ == "__main__":
    sys.exit(main())
