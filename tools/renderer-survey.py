#!/usr/bin/env python3
"""Tool code (tools/README.md, "The renderer survey"): measures every scene of scripts/*.json against the goal of epic
ter-8hyd: frame time, specks and blotches in stills, flicker in motion, and the draws until the light settles.

Usage, inside the dev shell from the repository root:
    python3 -I tools/renderer-survey.py probe-build <out>
    python3 -I tools/renderer-survey.py run <out> [--parts timing,still,motion,switch,lag,walk,stand,climb] [--only NAME ...]
    python3 -I tools/renderer-survey.py trace <out> [--slowest 5]
    python3 -I tools/renderer-survey.py run <out> --parts evidence --only rect-lamps [--skip 64] [--keep-capture]
    python3 -I tools/renderer-survey.py evidence <capture folder> [--skip 64] [--sweep <json>] [--write <json>]
    python3 -I tools/renderer-survey.py report <out>
    python3 -I tools/renderer-survey.py sheet <out>

tools/renderer-survey/cases.json lists the cases. "run" builds the Release demo, writes a script for each case into
<out>/scripts, renders it headless under the GPU lock (tools/gpu-lock.sh), measures the images, deletes them but for
the few that it keeps, and writes one result file per case into <out>/results/<part>/<name>.json. A case whose result
file exists does not run again, so a stopped run goes on where it stopped; delete the file to run the case again.
The part "evidence" runs only when --parts names it: it captures the block test's sums in a long still, and the
command "evidence" measures a kept capture (tools/README.md, "The block test's evidence"). "report" prints the tables and
the verdicts. "sheet" writes <out>/contact-sheet.png. "trace" records a GPU trace of the
slowest views (tools/gpu-trace.sh). "probe-build" builds the probe, which every part but timing needs: a copy of HEAD
with tools/renderer-survey/probe.patch, in <out>/probe-bin. The probe never enters src.

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
PARTS = {"timing": "stills", "still": "stills", "motion": "motion", "switch": "switches", "lag": "lags", "walk": "walks",
         "stand": "stands", "climb": "climbs"}
# The parts that run only when --parts names them, and the lists of cases.json whose views they take: the evidence of the
# block test (take T3) runs on the still views and on the switches' views with their lamps on, such as the orbit cabin.
NAMED_PARTS = {"evidence": ("stills", "switches")}


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


# The stand film of ticket ter-g3tk (/private/tmp/g3tk/run_dgate.sh): the walk's view without the walk, one light signal
# alone, a still and then one film frame every tick, against the film's own end level. In the orbit cabin, the sun turns
# against the ship at world ticks 300 to 306, and the bounce light that its sunlit sill strips give falls by 35 to 40 %
# (docs/plans/bounce-light-confidence.md, section 10). The case's settle counts from its settleTick.


def stand_script(case: dict) -> dict:
    """The still, the film of case["film"] frames, and case["end"] continued stills right after the film's last frame."""
    return scene_script(case["scene"], case["camera"], case["ticks"], [
        {"do": "capture", "name": "still"},
        {"do": "film", "name": "f", "frames": case["film"], "every": 1},
        *captures("end", case["end"]),
    ])


def stand_frame(case: dict, tick: int) -> int:
    """The stand film's frame that shows world tick `tick`: frame k shows tick case["ticks"] + k + 1."""
    return tick - case["ticks"] - 1


# The climb test of ticket ter-g3tk (/private/tmp/g3tk/look/seeds.json): continued stills of fixed states of the world,
# one light signal alone, with the diffuse filter and without it. At a fixed state, the bounce light of the cabin's
# far-ceiling band climbed to about 1.6 times its light without the filter over about 200 draws (section 10 of the
# design). Each state's first still comes STILL_TICKS or more after the last drawn frame, so it starts a new history.


def climb_script(case: dict) -> dict:
    """For each world tick of case["states"], case["stills"] continued stills t<tick>-s00 ..."""
    steps = []
    done = 0
    for tick in case["states"]:
        steps += run_step(tick - done)
        steps += captures(f"t{tick}", case["stills"])
        done = tick
    return scene_script(case["scene"], case["camera"], 0, steps)


def climb_runs(case: dict, exposure: str) -> dict:
    """The two probe runs of a climb case and their environments. The filtered run keeps the renderer's kernel, which
    TERTIUS_DIFFUSE_FILTER selects (tools/README.md, "The diffuse filter's switch"); the other turns the filter off."""
    common = {"TERTIUS_PROBE_EXPOSURE": exposure, "TERTIUS_PROBE_SIGNAL": SIGNALS[case["signal"]], **STILL_CONTINUES}
    return {"filtered": common, "unfiltered": {**common, "TERTIUS_DIFFUSE_FILTER": "off"}}


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


# The names of the entries of a row of region_means: the whole image, then the cells r<row><column> of the grid. r11 is
# the centre cell, which holds the orbit cabin's far-ceiling band in the walk's view (ticket ter-g3tk).
REGION_NAMES = ["whole"] + [f"r{r}{c}" for r in range(GRID) for c in range(GRID)]


def region_index(name: str) -> int:
    return REGION_NAMES.index(name)


def mean_rows(rows: list) -> list:
    """The mean of rows of region means, region by region."""
    return [sum(row[r] for row in rows) / len(rows) for r in range(len(rows[0]))]


# The stand film's excess is the mean of each window of this many film draws (ticket ter-g3tk's tables of the stand).
STAND_WINDOW = 40


def stand_result(case: dict, film: list, end: list) -> dict:
    """The stand film's measures, from the region means of its film frames and of its end stills. A region's end level is
    the mean of the last half of the end stills, as /private/tmp/g3tk/walk_split.py took it. A window's excess is its
    mean light over the end level, less 1. A settle counts the draws until a region stays within SETTLE_TOLERANCE of its
    end level, from the film's first frame and from the frame that shows case["settleTick"]. The verdict reads the
    settle of case["region"] after that tick: acceptance 7 of docs/plans/bounce-light-confidence.md names r11, because
    r10 and r12 wander by about 5 % with the sampling fault of ticket ter-g3tk."""
    level = mean_rows(end[len(end) // 2:])
    excess = []
    for first in range(0, len(film), STAND_WINDOW):
        window = mean_rows(film[first:first + STAND_WINDOW])
        excess.append([window[r] / level[r] - 1 if level[r] > 0 else None for r in range(len(level))])

    def settles(first):
        return [settle_draws([row[r] for row in film], [level[r]] * len(film), first, len(film))
                for r in range(len(level))]

    start = stand_frame(case, case["settleTick"])
    after = settles(start)
    result = {"scene": case["scene"], "signal": case["signal"], "region": case["region"], "firstTick": case["ticks"] + 1,
              "settleTick": case["settleTick"], "settleFrame": start, "settleWindow": len(film) - start,
              "window": STAND_WINDOW, "end": level, "excess": excess, "settleDraws": settles(0),
              "settleDrawsAfterTick": after, "bandSettleDraws": after[region_index(case["region"])],
              "film": film, "endStills": end}
    result["failed"] = settle_verdict(result["bandSettleDraws"])
    return result


# The climb's yardstick is the band's light with the filter off, the mean of the unfiltered stills from this one on: from
# draw 198, after the filter-off light reaches its plateau by about draw 132 (t0-f7zv, take T2a).
CLIMB_PLATEAU_FIRST = 5
# The target of acceptance 11 of docs/plans/bounce-light-confidence.md: the band's peak lies within this share of its
# light with the filter off.
CLIMB_LIMIT = 0.10


def climb_result(case: dict, runs: dict) -> dict:
    """The climb's measures, from the region means of the stills of each state, runs[label][f"t{tick}"], for the labels of
    climb_runs. Each region's peak share is its largest light over a state's filtered stills, divided by its plateau: the
    mean of the state's unfiltered stills from CLIMB_PLATEAU_FIRST on. The last share takes the last filtered still. The
    verdict reads the band region's largest peak share over the states. Main's clamp loses part of the band's light, so
    the share, and not the band's light, compares a build with another (section 10 of the design)."""
    band = region_index(case["region"])
    plateau, peak, last = {}, {}, {}
    for tick in case["states"]:
        state = f"t{tick}"
        filtered = runs["filtered"][state]
        plateau[state] = mean_rows(runs["unfiltered"][state][CLIMB_PLATEAU_FIRST:])
        peak[state] = [max(row[r] for row in filtered) / p if p > 0 else None for r, p in enumerate(plateau[state])]
        last[state] = [filtered[-1][r] / p if p > 0 else None for r, p in enumerate(plateau[state])]
    shares = [peak[state][band] for state in peak]
    result = {"scene": case["scene"], "signal": case["signal"], "region": case["region"], "states": case["states"],
              "draws": [still_draws(True, k) for k in range(case["stills"])], "plateau": plateau, "peakShare": peak,
              "lastShare": last, "bandPeakShare": None if None in shares else max(shares), **runs}
    result["failed"] = climb_verdict(result["bandPeakShare"])
    return result


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


def climb_verdict(share: float | None) -> list:
    """A climb fails when the band's peak exceeds its light with the filter off by more than CLIMB_LIMIT. A band without
    light when the filter is off gives no share, so the case measured nothing."""
    if share is None:
        return [NOT_MEASURED]
    return ["climb"] if share > 1 + CLIMB_LIMIT else []


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
# The block test's evidence (docs/plans/bounce-light-confidence.md, section 8, take T3 of section 20). The probe's
# TERTIUS_A_BLOCK_CAPTURE keeps the block sums of every signal with a block test, every draw of a long still, and these
# functions form the test's windows offline: how often a still gives evidence, how well the sums predict the noise of
# the difference of the fast and the slow light, and how both change with the test's constants.

SHADERS = ROOT / "src" / "Tertius.Rendering" / "Shaders"
# The constants of the test, in their single home, and the cutoff light of exposure.glsl, which the test also reads.
BLOCK_CONSTANTS = {SHADERS / "accumulate_blocks.glsl": ("LeastStreams", "TailStreams", "BlockSigmas", "BlockNoiseScale"),
                   SHADERS / "exposure.glsl": ("CutoffRadiance",)}
GLSL_CONSTANT = re.compile(r"^const float (\w+) = ([0-9.eE+-]+)(?:\s*/\s*([0-9.eE+-]+))?;", re.M)
# The window sizes in blocks: 1 x 1, 3 x 3 and 7 x 7.
BLOCK_WINDOWS = (1, 3, 7)
# The draws after a reset that the measures leave out, while the slow history fills. The test of step 4 measured from
# draw 64 on (ScreenTraceRenderTests, StillPanelForFourHundredDraws).
EVIDENCE_SKIP = 64
# The fewest measured draws of a window position whose deviation over the draws counts.
DEVIATION_DRAWS = 64
# The firing windows that the result lists, at the capture's own rule.
FIRES_KEPT = 100
# Acceptance 12 of the design: at most 1e-4 false windows per block-draw, and a measured over predicted deviation of the
# difference from 0.8 to 1.25.
EVIDENCE_LIMIT = 1e-4
DEVIATION_RANGE = (0.8, 1.25)
# The offline sweep: every combination of these values. Each list holds the design's value, so one row repeats the
# capture's own rule where the capture keeps the design's constants. The floors and tails reach past the design's,
# because the smoke run of rect-lamps (2026-11-12, t0-9a9e) still gave 2.4e-3 false windows per block-draw at a floor
# of 1/6 and 2000 tail streams.
EVIDENCE_SWEEP = {"blockSigmas": [3.0, 3.5, 4.0, 5.0], "tailStreams": [250.0, 500.0, 1000.0, 2000.0, 4000.0],
                  "noiseScale": [1.0, 1.25, 1.5, 2.0],
                  "changeShare": {"diffuse": [1 / 16, 1 / 12, 1 / 8, 1 / 6, 1 / 4, 1 / 3],
                                  "specular": [1 / 40, 1 / 30, 1 / 20, 1 / 12, 1 / 8]}}


def block_constants() -> dict:
    """The block test's constants as the shaders hold them."""
    found = {}
    for path, names in BLOCK_CONSTANTS.items():
        values = {m[1]: float(m[2]) / (float(m[3]) if m[3] else 1.0) for m in GLSL_CONSTANT.finditer(path.read_text())}
        for name in names:
            if name not in values:
                raise ValueError(f"{path} holds no constant {name}.")
            found[name] = values[name]
    return found


def difference_share(wf: float, ws: float) -> float:
    """κ of accumulate_blocks.glsl: the deviation of F − S for one stream as a share of the samples' deviation."""
    return math.sqrt(max(wf / (2 - wf) + ws / (2 - ws) - 2 * wf * ws / (wf + ws - wf * ws), 0.0))


def window_sums(texels, size: int):
    """The sums of the size x size blocks around each block of texels (H, W, 4), 0 outside the image, as the spatial
    dispatch forms them."""
    r = size // 2
    high, wide = texels.shape[:2]
    padded = np().pad(texels, ((r, r), (r, r), (0, 0)))
    rows = sum(padded[k:k + high] for k in range(size))
    return sum(rows[:, k:k + wide] for k in range(size))


def window_fires(window, rule: dict):
    """windowFires of accumulate_blocks.glsl for windows (..., 4) of (sum d, sum v, n, sum s). The rule's numbers can be
    arrays that broadcast against the windows. Its varianceScale multiplies sum v: the square of another BlockNoiseScale
    over the capture's."""
    n_ = np()
    d, v, n, s = (window[..., k] for k in range(4))
    v = v * rule["varianceScale"]
    fires = ((n >= rule["leastStreams"]) & (s * s >= rule["tailStreams"] * v)
             & (d * d >= rule["blockSigmas"] * rule["blockSigmas"] * v)
             & (n_.abs(d) >= rule["changeShare"] * n_.maximum(s, s + d)) & (n_.abs(d) >= n * rule["cutoff"]))
    return fires & (d < 0) if rule["oneSided"] else fires


def read_capture(folder: Path) -> tuple[dict, list, list]:
    """The probe's capture: its header, the (draw, draw index since the last reset, reset) of each record, and for each
    signal its header entry and its records (draws, H, W, 4). A record that a stopped run left half written is left out."""
    n = np()
    header = json.loads((folder / "header.json").read_text())
    draws = [tuple(int(v) for v in line.split()) for line in (folder / "draws.txt").read_text().splitlines() if line]
    shape = (header["blocksHigh"], header["blocksWide"], 4)
    per = shape[0] * shape[1] * 4
    signals = []
    for entry in header["signals"]:
        path = folder / entry["file"]
        count = path.stat().st_size // (4 * per)
        data = n.memmap(path, dtype="<f4", mode="r", shape=(count, *shape)) if count else n.zeros((0, *shape), "<f4")
        signals.append((entry, data))
    records = min([len(draws)] + [len(data) for _, data in signals])
    return header, draws[:records], [(entry, data[:records]) for entry, data in signals]


def capture_rule(entry: dict, constants: dict) -> dict:
    """The rule that the capture's build ran for one signal: the probe's push constants where it set them (-1 keeps the
    shader's constant), the signal's floor, and a one-sided test without a widened box (section 9)."""
    def chosen(pushed, constant):
        return pushed if pushed >= 0 else constant
    return {"leastStreams": constants["LeastStreams"],
            "tailStreams": chosen(entry["probeTailStreams"], constants["TailStreams"]),
            "blockSigmas": chosen(entry["probeBlockSigmas"], constants["BlockSigmas"]),
            "noiseScale": chosen(entry["probeNoiseScale"], constants["BlockNoiseScale"]),
            "changeShare": entry["changeShare"], "cutoff": constants["CutoffRadiance"],
            "oneSided": entry["retainedSigmas"] <= 0, "varianceScale": 1.0}


def sweep_rules(rule: dict, light: str, sweep: dict) -> tuple[list, dict]:
    """The sweep's rows and their rule, with each number a column (rows, 1) that broadcasts against the windows."""
    n = np()
    rows = [{"blockSigmas": b, "tailStreams": t, "noiseScale": s, "changeShare": c}
            for b in sweep["blockSigmas"] for t in sweep["tailStreams"] for s in sweep["noiseScale"]
            for c in sweep["changeShare"][light]]
    if rule["noiseScale"] <= 0 and any(r["noiseScale"] != rule["noiseScale"] for r in rows):
        raise ValueError("A capture with a noise scale of 0 holds no predicted variance to scale.")
    column = lambda values: n.array(values, float)[:, None]
    swept = {**rule, "blockSigmas": column([r["blockSigmas"] for r in rows]),
             "tailStreams": column([r["tailStreams"] for r in rows]),
             "changeShare": column([r["changeShare"] for r in rows]),
             "varianceScale": column([(r["noiseScale"] / rule["noiseScale"]) ** 2 if rule["noiseScale"] > 0 else 1.0
                                      for r in rows])}
    loosest = {**rule, **{k: swept[k].min() for k in ("blockSigmas", "tailStreams", "changeShare", "varianceScale")}}
    return rows, swept, loosest


def quantiles(values, points) -> dict:
    n = np()
    values = n.asarray(values, float)
    if not values.size:
        return {name: None for name, _ in points}
    return {name: float(n.quantile(values, q)) for name, q in points}


def sizes_dict(make) -> dict:
    return {str(size): make(size) for size in BLOCK_WINDOWS}


def signal_evidence(entry: dict, records, draws: list, every: int, constants: dict, skip: int, sweep: dict) -> dict:
    """The measures of one signal's captured block sums. A block-draw is a block with a contributing stream in a measured
    draw, and a fire counts at such a block only, because evidence acts on the block's contributing pixels alone."""
    n = np()
    rule = capture_rule(entry, constants)
    rows, swept, loosest = sweep_rules(rule, entry["light"], sweep)
    high, wide = records.shape[1:3]
    fires = {key: 0 for key in [*map(str, BLOCK_WINDOWS), "any"]}
    swept_fires = n.zeros((len(rows), len(BLOCK_WINDOWS) + 1), n.int64)
    kept = []
    block_draws = repeats = longest = measured = 0
    run = n.zeros((high, wide), n.int64)
    last = None  # the draw index of the last measured record
    zeros = lambda: n.zeros((high, wide))
    deviation = {size: [zeros(), zeros(), zeros(), zeros()] for size in BLOCK_WINDOWS}  # draws, sum m, sum m², sum pred
    stationary = {size: [zeros(), zeros(), zeros()] for size in BLOCK_WINDOWS}  # draws, sum D, sum S
    spread = [zeros(), zeros(), zeros()]  # sum v, sum n, sum s of the 1 x 1 window
    for k, (draw, index, _) in enumerate(draws):
        if index < skip:
            continue
        texels = n.asarray(records[k], float)
        contributing = texels[..., 2] > 0
        block_draws += int(contributing.sum())
        fired = n.zeros((high, wide), bool)
        candidates = []
        for column, size in enumerate(BLOCK_WINDOWS):
            window = window_sums(texels, size)
            d, v, count, s = (window[..., c] for c in range(4))
            hit = window_fires(window, rule) & contributing
            fires[str(size)] += int(hit.sum())
            fired |= hit
            for y, x in zip(*n.nonzero(hit)):
                if len(kept) < FIRES_KEPT:
                    kept.append({"record": k, "draw": draw, "drawIndex": index, "x": int(x), "y": int(y), "window": size,
                                 "n": float(count[y, x]), "dOverS": float(d[y, x] / s[y, x]) if s[y, x] else None,
                                 "z": float(d[y, x] / math.sqrt(v[y, x])) if v[y, x] > 0 else None,
                                 "tail": float(s[y, x] ** 2 / v[y, x]) if v[y, x] > 0 else None})
            full = count >= rule["leastStreams"]
            mean = n.where(full, d / n.maximum(count, 1), 0.0)
            draws_, sum_m, sum_m2, sum_pred = deviation[size]
            draws_ += full
            sum_m += mean
            sum_m2 += mean * mean
            sum_pred += n.where(full, v / n.maximum(count, 1) ** 2, 0.0)
            stationary[size][0] += full
            stationary[size][1] += n.where(full, d, 0.0)
            stationary[size][2] += n.where(full, s, 0.0)
            if size == 1:
                spread[0] += v
                spread[1] += count
                spread[2] += s
            loose = window_fires(window, loosest) & contributing
            ys, xs = n.nonzero(loose)
            candidates.append((ys * wide + xs, n.full(len(ys), column), window[ys, xs]))
        fires["any"] += int(fired.sum())
        follows = last is not None and index == last + every
        if follows:
            repeats += int((fired & (run > 0)).sum())
        run = n.where(fired, (run if follows else 0) + 1, 0)
        longest = max(longest, int(run.max()))
        last = index
        measured += 1
        blocks = n.concatenate([c[0] for c in candidates])
        if blocks.size:
            columns = n.concatenate([c[1] for c in candidates])
            windows = n.concatenate([c[2] for c in candidates])
            passes = window_fires(windows[None], swept)
            for column in range(len(BLOCK_WINDOWS)):
                swept_fires[:, column] += passes[:, columns == column].sum(axis=1)
            order = n.argsort(blocks, kind="stable")
            _, starts = n.unique(blocks[order], return_index=True)
            swept_fires[:, -1] += n.logical_or.reduceat(passes[:, order], starts, axis=1).sum(axis=1)

    rate = lambda count: count / block_draws if block_draws else None

    def deviation_of(size):
        draws_, sum_m, sum_m2, sum_pred = deviation[size]
        use = (draws_ >= DEVIATION_DRAWS) & (sum_pred > 0)
        if not use.any():
            return {"pooled": None, "p10": None, "p50": None, "p90": None, "positions": 0}
        variance = n.maximum(sum_m2[use] - sum_m[use] ** 2 / draws_[use], 0.0)
        ratios = n.sqrt(variance / sum_pred[use])
        return {"pooled": float(math.sqrt(variance.sum() / sum_pred[use].sum())),
                **quantiles(ratios, (("p10", 0.1), ("p50", 0.5), ("p90", 0.9))), "positions": int(use.sum())}

    def stationary_of(size):
        draws_, sum_d, sum_s = stationary[size]
        use = (draws_ > 0) & (sum_s > 0)
        ratio = sum_d[use] / sum_s[use]
        result = quantiles(n.abs(ratio), (("p50", 0.5), ("p90", 0.9), ("p99", 0.99), ("p99.9", 0.999),
                                          ("p99.99", 0.9999), ("max", 1.0)))
        return {**result, "mean": float(ratio.mean()) if ratio.size else None, "positions": int(use.sum())}

    deviations = sizes_dict(deviation_of)
    # The reflections' precise history is uncapped by default: κ of a history without end, 0.378.
    kappa = difference_share(1 / entry["fastCap"], 0.0 if entry.get("uncapped") else 1 / entry["cap"])
    use = (spread[1] > 0) & (spread[2] > 0)
    cv = n.sqrt(spread[0][use] * spread[1][use]) / spread[2][use] / (kappa * rule["noiseScale"]) \
        if rule["noiseScale"] > 0 else n.zeros(0)
    names = [*map(str, BLOCK_WINDOWS), "any"]
    return {
        "light": entry["light"], "part": entry["part"], "rule": rule, "kappaAtCap": kappa,
        "records": len(draws), "measuredRecords": measured, "blockDraws": block_draws,
        "fires": fires, "falsePerBlockDraw": {key: rate(count) for key, count in fires.items()},
        "repeats": repeats, "repeatShare": repeats / fires["any"] if fires["any"] else None, "longestRun": longest,
        "deviationRatio": deviations,
        "noiseScaleForRatio1": {size: d["pooled"] * rule["noiseScale"] if d["pooled"] is not None else None
                                for size, d in deviations.items()},
        "cv": {**quantiles(cv, (("p10", 0.1), ("p50", 0.5), ("p90", 0.9), ("p99", 0.99))), "blocks": int(use.sum())},
        "stationaryDifference": sizes_dict(stationary_of),
        "firesKept": kept,
        "sweep": [{**row, "fires": {name: int(swept_fires[i, j]) for j, name in enumerate(names)},
                   "falsePerBlockDraw": {name: rate(int(swept_fires[i, j])) for j, name in enumerate(names)}}
                  for i, row in enumerate(rows)],
    }


def evidence(folder: Path, skip: int = EVIDENCE_SKIP, sweep: dict | None = None) -> dict:
    """The measures of take T3 for a capture folder of the probe, for each signal that it holds."""
    constants = block_constants()
    header, draws, signals = read_capture(folder)
    return {"capture": {k: v for k, v in header.items() if k != "signals"}, "constants": constants, "skip": skip,
            "signals": [signal_evidence(entry, records, draws, header["every"], constants, skip, sweep or EVIDENCE_SWEEP)
                        for entry, records in signals]}


def evidence_verdict(result: dict) -> list:
    """Acceptance 12 for each signal: its false evidence, and its measured over predicted deviation of the difference in
    the 1 x 1 window."""
    failed = []
    for signal in result["signals"]:
        rate = signal["falsePerBlockDraw"]["any"]
        if rate is not None and rate > EVIDENCE_LIMIT:
            failed.append(f"{signal['light']} evidence")
        ratio = signal["deviationRatio"]["1"]["pooled"]
        if ratio is not None and not DEVIATION_RANGE[0] <= ratio <= DEVIATION_RANGE[1]:
            failed.append(f"{signal['light']} noise")
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


def probe_series(work: Path, pattern: str) -> list:
    """The region means of each probe capture that matches pattern, in name order, and deletes the captures."""
    rows = []
    for f in sorted(work.glob(pattern)):
        rows.append(region_means(probe_luminance(f)))
    return rows


# The environment variable that limits a call of run_switch to some of a case's runs, by label, comma-separated, such
# as "switch" or "off,on". One switch run is 460 film frames, so the three runs of a case do not fit in one workbench
# job of 840 s. Each run's series is kept in <out>/series/switch/<case>-<label>.json, and a run whose series is kept does
# not render again. The call that finds all three series measures them; one that does not returns None.
SWITCH_RUNS = "TERTIUS_SURVEY_SWITCH_RUNS"


def run_switch(out: Path, case: dict, args):
    series = {}
    wanted = os.environ.get(SWITCH_RUNS)
    for label, env in switch_runs(case).items():
        kept = out / "series" / "switch" / f"{case['name']}-{label}.json"
        if kept.exists():
            series[label] = json.loads(kept.read_text())
            continue
        if wanted and label not in wanted.split(","):
            continue
        work = out / "work" / "switch" / f"{case['name']}-{label}"
        render(work, switch_script(case), binary=probe_binary(out),
               env={"TERTIUS_PROBE_EXPOSURE": args.exposure, **env}, extra=PROBE_OPTIONS)
        series[label] = probe_series(work, "f-*.png")
        kept.parent.mkdir(parents=True, exist_ok=True)
        kept.write_text(json.dumps(series[label]))
        if label == "switch":
            frames = sorted(work.glob("f-*.png"))
            images = out / "images" / "switch"
            for k in (SWITCH_OFF - 1, SWITCH_OFF + 8, SWITCH_ON - 1, SWITCH_ON + 8):
                keep_png(frames[k], images / f"{case['name']}-{k:03d}.png")
        shutil.rmtree(work)
    if len(series) < len(switch_runs(case)):
        return None
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
    save(out, "switch", case["name"], result)
    return result


def run_lag(out: Path, case: dict, args):
    binary = probe_binary(out)
    film_script, stills_script = lag_scripts(case)
    env = {"TERTIUS_PROBE_EXPOSURE": args.exposure}
    work = out / "work" / "lag" / f"{case['name']}-film"
    render(work, film_script, binary=binary, env=env, extra=PROBE_OPTIONS)
    film = probe_series(work, "f-*.png")
    shutil.rmtree(work)
    work = out / "work" / "lag" / f"{case['name']}-stills"
    render(work, stills_script, binary=binary, env={**env, **STILL_CONTINUES}, extra=PROBE_OPTIONS)
    stills = {k: region_means(probe_luminance(work / f"k{k:04d}-s03.png")) for k in case["samples"]}
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
    render(work, walk_script(case), binary=binary, env=env, extra=PROBE_OPTIONS)
    after = probe_series(work, "h-*.png")
    during = probe_series(work, "g-*.png")
    shutil.rmtree(work)
    work = out / "work" / "walk" / f"{case['name']}-control"
    render(work, walk_control_script(case), binary=binary, env=env, extra=PROBE_OPTIONS)
    film = probe_series(work, "c-*.png")
    control = [film[walk_control_frame(case, k)] for k in range(len(after))]
    shutil.rmtree(work)
    draws = walk_settle(after, control)
    result = {"scene": case["scene"], "settleDraws": slowest_settle(draws), "settleDrawsWhole": draws[0],
              "regionDraws": draws, "window": len(after), "after": after, "during": during, "control": control}
    result["failed"] = settle_verdict(result["settleDraws"])
    save(out, "walk", case["name"], result)
    return result



def probe_exposure(case: dict, args) -> str:
    """The probe's fixed exposure in stops: the case's own, or the run's --exposure."""
    return case.get("exposure", args.exposure)


def run_stand(out: Path, case: dict, args):
    work = out / "work" / "stand" / case["name"]
    env = {"TERTIUS_PROBE_EXPOSURE": probe_exposure(case, args), "TERTIUS_PROBE_SIGNAL": SIGNALS[case["signal"]],
           **STILL_CONTINUES}
    render(work, stand_script(case), binary=probe_binary(out), env=env, extra=PROBE_OPTIONS)
    film = probe_series(work, "f-*.png")
    end = probe_series(work, "end-s*.png")
    shutil.rmtree(work)
    result = stand_result(case, film, end)
    save(out, "stand", case["name"], result)
    return result


def run_climb(out: Path, case: dict, args):
    """The climb's two probe runs, one after the other: four 1920 x 1080 renders at once ran out of memory on the
    workbench's 16 GB card (t0-f7zv)."""
    runs = {}
    for label, env in climb_runs(case, probe_exposure(case, args)).items():
        work = out / "work" / "climb" / f"{case['name']}-{label}"
        render(work, climb_script(case), binary=probe_binary(out), env=env, extra=PROBE_OPTIONS)
        runs[label] = {f"t{tick}": probe_series(work, f"t{tick}-s*.png") for tick in case["states"]}
        shutil.rmtree(work)
    result = climb_result(case, runs)
    save(out, "climb", case["name"], result)
    return result


def evidence_env(capture: Path) -> dict:
    """The probe's settings of an evidence run: the capture folder, and the sums without their action unless the caller
    sets TERTIUS_A_BLOCK_ACTION itself. The history goes on through the still's captures, as in the still part."""
    return {"TERTIUS_A_BLOCK_CAPTURE": str(capture), "TERTIUS_A_BLOCK_ACTION": os.environ.get("TERTIUS_A_BLOCK_ACTION", "0"),
            **STILL_CONTINUES}


def run_evidence(out: Path, case: dict, args):
    """The long still of the still part, 28 captures of 33 draws to draw 924, with the block sums captured every draw.
    The capture of one signal takes about 230 KB per draw at 1280 x 720, so the run reduces it to numbers and deletes it,
    unless --keep-capture moves it to <out>/captures/<name>."""
    work = out / "work" / "evidence" / case["name"]
    capture = work / "blocks"
    env = evidence_env(capture)
    script, _ = still_script(*source_of(case))
    log = render(work, script, binary=probe_binary(out), env=env, extra=NO_BLOOM)
    knobs = {k: v for k, v in sorted({**os.environ, **env}.items())
             if k.startswith("TERTIUS_A_") and k != "TERTIUS_A_BLOCK_CAPTURE"}
    result = {"script": label_of(case), "scene": scene_name(case), "frame": case.get("frame", 0), "knobs": knobs,
              **evidence(capture, skip=args.skip)}
    result["failed"] = evidence_verdict(result)
    if args.keep_capture:
        kept = out / "captures" / case["name"]
        if kept.exists():
            shutil.rmtree(kept)
        kept.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(capture), str(kept))
    shutil.rmtree(work)
    log.unlink(missing_ok=True)
    save(out, "evidence", case["name"], result)
    return result


def cases_of(cases: dict, part: str) -> list:
    """The cases of a part: its list of cases.json, or for a part of NAMED_PARTS the views of its lists, the first case
    of each name."""
    if part in PARTS:
        return cases[PARTS[part]]
    chosen = {}
    for name in NAMED_PARTS[part]:
        for case in cases[name]:
            chosen.setdefault(case["name"], case)
    return list(chosen.values())


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
        pending = [c["name"] for c in cases_of(cases, part) if (not args.only or c["name"] in args.only)
                   and not done(out, part, c["name"])]
        for first in range(0, len(pending), args.chunk):
            chunk = pending[first:first + args.chunk]
            log = out / "logs" / f"{part}-{chunk[0]}.lock.log"
            log.parent.mkdir(parents=True, exist_ok=True)
            command = [sys.executable, "-I", str(Path(__file__).resolve()), "run", str(out), "--parts", part, "--no-build",
                       "--chunk", "0", "--exposure", args.exposure, "--skip", str(getattr(args, "skip", EVIDENCE_SKIP)),
                       "--only", *chunk]
            if getattr(args, "keep_capture", False):
                command += ["--keep-capture"]
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
               "walk": run_walk, "stand": run_stand, "climb": run_climb, "evidence": run_evidence}
    failures = 0
    for part in parts:
        runner = runners[part]
        for case in cases_of(cases, part):
            if args.only and case["name"] not in args.only:
                continue
            if done(out, part, case["name"]):
                continue
            print(f"{part} {case['name']} ...", flush=True)
            try:
                result = runner(out, case, args)
                if result is None:
                    print(f"{part} {case['name']}: some runs kept, not measured yet", flush=True)
                    continue
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
    for name, r in results(out, "stand").items():
        window = r["settleWindow"]
        lines.append(f"| {name} {r['region']} after tick {r['settleTick']} | stand "
                     f"| {settle_text(r['bandSettleDraws'], window)} | {settle_text(r['settleDrawsAfterTick'][0], window)} "
                     f"| {'fail' if r['failed'] else 'pass'} |")
    lines += ["", "| Climb | Band | State | Peak share | Last share | Verdict |", "|---|---|---|---:|---:|---|"]
    for name, r in results(out, "climb").items():
        band = region_index(r["region"])
        verdict = "fail: " + ", ".join(r["failed"]) if r["failed"] else "pass"
        for tick in r["states"]:
            state = f"t{tick}"
            lines.append(f"| {name} | {r['region']} | {state} | {cell(r['peakShare'][state][band], 3)} "
                         f"| {cell(r['lastShare'][state][band], 3)} | {verdict} |")
    evidence_results = results(out, "evidence")
    if evidence_results:
        lines += ["", "| Evidence | Signal | Block-draws | False 1 × 1 | False 3 × 3 | False 7 × 7 | False any | Repeat share "
                  "| Ratio 1 × 1 | Ratio 3 × 3 | Ratio 7 × 7 | CV p50 | F − S p99.99 | Verdict |",
                  "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|"]
    for name, r in evidence_results.items():
        verdict = "fail: " + ", ".join(r["failed"]) if r["failed"] else "pass"
        for s in r["signals"]:
            rate = lambda key: f"{s['falsePerBlockDraw'][key]:.1e}" if s["falsePerBlockDraw"][key] is not None else "n/a"
            ratio = lambda size: cell(s["deviationRatio"][size]["pooled"])
            tails = [d["p99.99"] for d in s["stationaryDifference"].values() if d.get("p99.99") is not None]
            lines.append(f"| {name} | {s['light']} | {s['blockDraws']} | {rate('1')} | {rate('3')} | {rate('7')} "
                         f"| {rate('any')} | {cell(s['repeatShare'])} | {ratio('1')} | {ratio('3')} | {ratio('7')} "
                         f"| {cell(s['cv']['p50'])} | {cell(max(tails) if tails else None, 3)} | {verdict} |")
    print("\n".join(lines))
    return 0


def evidence_command(args):
    sweep = json.loads(Path(args.sweep).read_text()) if args.sweep else None
    result = evidence(Path(args.capture), skip=args.skip, sweep=sweep)
    text = json.dumps(result, indent=1)
    if args.write:
        Path(args.write).write_text(text)
    else:
        print(text)
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
    p.add_argument("--skip", type=int, default=EVIDENCE_SKIP,
                   help="evidence: the draws after a reset that the measures leave out")
    p.add_argument("--keep-capture", action="store_true",
                   help="evidence: keep the block sums in <out>/captures/<name> for the evidence command")
    p = sub.add_parser("evidence", help="the measures of take T3 for a capture folder of the probe")
    p.add_argument("capture")
    p.add_argument("--skip", type=int, default=EVIDENCE_SKIP, help="the draws after a reset that the measures leave out")
    p.add_argument("--sweep", help="a JSON file of the sweep's values, in the form of EVIDENCE_SWEEP")
    p.add_argument("--write", help="the JSON file to write; without it the numbers go to the standard output")
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
            "split-report": split_report, "evidence": evidence_command}[args.command](args)


if __name__ == "__main__":
    sys.exit(main())
