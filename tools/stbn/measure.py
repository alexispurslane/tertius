# Research prototype for ticket ter-s97m, run on the GPU workbench by tools/stbn/job.sh in the job's work folder:
#   python3 -I cut/tools/stbn/measure.py <case> <out> [<crop x> <crop y>]
# Renders one survey still view with the probe in the trees main and cut, at 1920 x 1080 under FSR 3.1, with the
# history kept through the captures (33 draws apart) and a fixed exposure, and scores each run against main's long
# mean. Writes <out>/<case>.json with the scores and <out>/<case>-*.png crops of 960 x 540 in display gray.
#
# The truth of a signal is main's mean over captures 30 to 59 (draws about 1,000 to 2,000). A run's error in one
# signal goes into the whole image's truth: shown = truth_all - truth_signal + run_signal, and both pass through the
# probe's Hable curve to display light, as the survey's stills do. On that display image:
#   daub  = daub.py of ticket t7-3e22: the 90th percentile over smooth pixels of the relative error between the 2 to 24
#           pixel scales, after a 5 x 5 median; clouds stop showing at about 0.010
#   grain = the 90th percentile over visible pixels of |rel - gauss(rel, 3 px)|, the error below 3 pixels
#   mean error = the 90th percentile over visible pixels of |gauss(rel, 3 px)| of the mean of captures 10 to 29 (draws
#           about 360 to 990): the error that a long average keeps. For main, that window and the truth's are disjoint,
#           so main's value is the floor of the measure.
import json
import os
from concurrent.futures import ThreadPoolExecutor
import shutil
import subprocess
import sys
import importlib.util
from pathlib import Path

import numpy as np
from PIL import Image
from scipy.ndimage import binary_erosion, gaussian_filter, median_filter

WORK = Path.cwd()
spec = importlib.util.spec_from_file_location("survey", WORK / "main" / "tools" / "renderer-survey.py")
sv = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sv)

SHOWN, SETTLED = 1, 9           # capture indices: about 60 and 330 draws
WINDOW = range(10, 30)          # the long mean of a run
TRUTH = range(30, 60)           # main's truth
CLASSES = {"smooth": (0.0, 0.15), "glossy": (0.15, 0.35), "rough": (0.35, 0.5)}


# The scale of the scene's light before the display curve: the truth's log-average maps to 0.18, as an auto exposure
# would place it. measure() sets it from main's truth.
SCALE = [1.0]


def display(y):
    """The survey's display(): the ACES fit of tonemap.glsl on the luminance, after SCALE."""
    return sv.display(np.maximum(y, 0.0) * SCALE[0])


def render(tree, case_name, signal, captures, env):
    case = next(c for c in sv.load_cases()["stills"] if c["name"] == case_name)
    # sv.still_script with its own count of captures: the threads must not share the survey's global count.
    source, frame = sv.source_of(case)
    script, still = sv.views_script(source, {frame: sv.captures("v", captures)})
    first_is_still = still[frame]
    out = WORK / "render" / f"{tree}-{signal}-{env.get('TERTIUS_STBN', 'off')}-{env.get('TERTIUS_STBN_SHIFT', 'off')}-{env.get('TERTIUS_STBN_CONTROL', 'off')}"
    shutil.rmtree(out, ignore_errors=True)
    out.mkdir(parents=True)
    (out / "script.json").write_text(json.dumps(script))
    run_env = {**os.environ, "TERTIUS_PROBE_STILL_CONTINUES": "1", "TERTIUS_PROBE_EXPOSURE": "4.08",
               "TERTIUS_PROBE_SIGNAL": str(signal), **env}
    binary = WORK / tree / "src" / "Tertius.Demo" / "bin" / "Release" / "net11.0" / "Tertius.Demo"
    command = [str(binary), "--script", str(out / "script.json"), "--headless", "--out", str(out), "--size",
               "1920x1080", "--bloom", "off", "--tone-curve", "hable"]
    done = subprocess.run(command, cwd=WORK / tree, env=run_env, capture_output=True, text=True)
    if done.returncode != 0:
        raise SystemExit(f"{out.name} failed:\n{done.stdout[-2000:]}\n{done.stderr[-2000:]}")
    draws = [sv.still_draws(first_is_still, k) for k in range(captures)]
    return out, draws


class Run:
    """The captures of one render that the scores need: the shown and settled ones, the window's mean, the truth's."""

    def __init__(self, tree, case, signal, captures, env):
        out, self.draws = render(tree, case, signal, captures, env)
        load = lambda k: sv.probe_luminance(out / f"v-s{k:02d}.png").astype(np.float64)
        self.first = [load(0), load(1)] if captures >= 2 else [load(0)]
        self.shown = load(SHOWN) if captures > SHOWN else None
        self.settled = load(SETTLED) if captures > SETTLED else None
        self.window = sum(load(k) for k in WINDOW) / len(WINDOW) if captures >= WINDOW.stop else None
        self.truth = sum(load(k) for k in TRUTH) / len(TRUTH) if captures >= TRUTH.stop else None
        shutil.rmtree(out)


def smooth_mask(r):
    rb = gaussian_filter(r, 3)
    gy, gx = np.gradient(rb)
    smooth = (np.hypot(gx, gy) / np.maximum(rb, 0.01) < 0.02) & (rb > 0.002)
    return binary_erosion(smooth, iterations=6)


def scores(s, r, smooth, visible, regions):
    """daub, grain and the blurred error of display image s against display truth r, per region mask."""
    level = np.median((gaussian_filter(s, 3) / np.maximum(gaussian_filter(r, 3), 1e-3))[smooth])
    rel_all = s / level / np.maximum(r, 0.01) - 1
    rel = np.where(smooth, median_filter(np.where(smooth, rel_all, 0), size=5), 0)
    w = gaussian_filter(smooth.astype(float), 3)
    w36 = gaussian_filter(smooth.astype(float), 36)
    band = gaussian_filter(rel, 3) / np.maximum(w, 1e-3) - gaussian_filter(rel, 36) / np.maximum(w36, 1e-3)
    vis_rel = np.where(visible, rel_all, 0)
    wv = gaussian_filter(visible.astype(float), 3)
    blurred = gaussian_filter(vis_rel, 3) / np.maximum(wv, 1e-3)
    fine = vis_rel - blurred
    result = {}
    for name, region in regions.items():
        sm, vi = smooth & region, visible & region
        result[name] = {
            "daub": float(np.percentile(np.abs(band[sm]), 90)) if sm.sum() > 500 else None,
            "grain": float(np.percentile(np.abs(fine[vi]), 90)) if vi.sum() > 500 else None,
            "blurred": float(np.percentile(np.abs(blurred[vi]), 90)) if vi.sum() > 500 else None,
            "smoothPixels": int(sm.sum()), "visiblePixels": int(vi.sum()),
        }
    return result


def crop_png(y, path, x0, y0):
    d = display(y)[y0:y0 + 540, x0:x0 + 960]
    srgb = np.where(d <= 0.0031308, d * 12.92, 1.055 * np.power(d, 1 / 2.4) - 0.055)
    Image.fromarray((np.clip(srgb, 0, 1) * 255 + 0.5).astype(np.uint8)).save(path, optimize=True)


def main():
    case, out = sys.argv[1], Path(sys.argv[2])
    x0, y0 = (int(sys.argv[3]), int(sys.argv[4])) if len(sys.argv) > 4 else (480, 270)
    out.mkdir(parents=True, exist_ok=True)
    result = {"case": case}
    # Every render at once, three at a time: a capture costs about 3 s of CPU for its PNG, the worker has 48 cores, and six renderers ran out of the 16 GB card.
    plan = {
        "main-all": ("main", 0, 60, {}), "rough": ("main", 4, 2, {}), "aa": ("cut", 0, 2, {"TERTIUS_STBN": "off"}),
        "C": ("cut", 0, 30, {"TERTIUS_STBN": "both"}),
        "C-shift": ("cut", 0, 30, {"TERTIUS_STBN": "both", "TERTIUS_STBN_SHIFT": "on"}),
        "main-bounce": ("main", 2, 60, {}), "A": ("cut", 2, 30, {"TERTIUS_STBN": "diffuse"}),
        "A-shift": ("cut", 2, 30, {"TERTIUS_STBN": "diffuse", "TERTIUS_STBN_SHIFT": "on"}),
        "main-refl": ("main", 3, 60, {}), "B": ("cut", 3, 30, {"TERTIUS_STBN": "reflection"}),
        "B-shift": ("cut", 3, 30, {"TERTIUS_STBN": "reflection", "TERTIUS_STBN_SHIFT": "on"}),
    }
    if os.environ.get("STBN_PLAN") == "controls":
        plan = {name: plan[name] for name in ("main-all", "rough", "aa", "main-bounce", "A", "main-refl", "B")}
        for kind, signal, label in (("diffuse", 2, "A"), ("reflection", 3, "B")):
            plan[f"{label}-W1"] = ("cut", signal, 30, {"TERTIUS_STBN": kind, "TERTIUS_STBN_CONTROL": "periodic"})
            plan[f"{label}-W2"] = ("cut", signal, 30, {"TERTIUS_STBN": kind, "TERTIUS_STBN_CONTROL": "white"})
    if os.environ.get("STBN_PLAN") == "build":
        # The built branch draws its reflection rays from the mask with no switch: cut against main.
        plan = {name: plan[name] for name in ("main-all", "rough", "main-refl")}
        plan["B"] = ("cut", 3, 30, {})
        plan["C"] = ("cut", 0, 30, {})
    with ThreadPoolExecutor(3) as pool:
        futures = {name: pool.submit(Run, tree, case, signal, captures, env)
                   for name, (tree, signal, captures, env) in plan.items()}
        runs = {name: f.result() for name, f in futures.items()}
    print("renders done", flush=True)
    main_all = runs["main-all"]
    result["draws"] = {"shown": main_all.draws[SHOWN], "settled": main_all.draws[SETTLED],
                       "window": [main_all.draws[WINDOW.start], main_all.draws[WINDOW.stop - 1]],
                       "truth": [main_all.draws[TRUTH.start], main_all.draws[TRUTH.stop - 1]]}
    truth_all = main_all.truth
    lit = truth_all[truth_all > 0]
    SCALE[0] = 0.18 / float(np.exp(np.mean(np.log(lit))))
    result["scale"] = SCALE[0]
    rough = runs["rough"].first[1]
    dt = display(truth_all)
    smooth = smooth_mask(dt)
    visible = gaussian_filter(dt, 3) > 0.002
    reflective = {name: (rough > lo) & (rough <= hi) for name, (lo, hi) in CLASSES.items()}
    whole = {"all": np.ones_like(smooth)}
    result["roughnessShare"] = {name: float(m[visible].mean()) for name, m in reflective.items()}

    # The A/A check: the cut tree with the mask off draws main's bits.
    if "aa" in runs:
        aa = runs["aa"]
        result["sameBitsWithMaskOff"] = bool(all(np.array_equal(a, b) for a, b in zip(aa.first, main_all.first)))

    def score_run(run, truth_signal, regions):
        inject = lambda y: display(truth_all - truth_signal + y) if truth_signal is not None else display(y)
        return {"shown": scores(inject(run.shown), dt, smooth, visible, regions),
                "settled": scores(inject(run.settled), dt, smooth, visible, regions),
                "mean": scores(inject(run.window), dt, smooth, visible, regions)}

    def keep(name, run, truth_signal, which="shown"):
        y = getattr(run, which)
        crop_png(truth_all - truth_signal + y if truth_signal is not None else y, out / f"{case}-{name}.png", x0, y0)

    crop_png(truth_all, out / f"{case}-truth.png", x0, y0)
    result["all"] = {"main": score_run(main_all, None, whole)}
    keep("main-all", main_all, None)
    keep("main-all-settled", main_all, None, "settled")
    for label in [name for name in ("C", "C-shift") if name in runs]:
        run = runs[label]
        result["all"][label] = score_run(run, None, whole)
        keep(f"{label}-all", run, None)
        if label == "C":
            keep("C-all-settled", run, None, "settled")
        print(label, "done", flush=True)

    if "main-bounce" in runs:
        main_bounce = runs["main-bounce"]
        result["bounce"] = {"main": score_run(main_bounce, main_bounce.truth, whole)}
        for label in [name for name in runs if name.startswith("A")]:
            result["bounce"][label] = score_run(runs[label], main_bounce.truth, whole)
        keep("main-bounce", main_bounce, main_bounce.truth)
        keep("A-bounce", runs["A"], main_bounce.truth)
        print("bounce done", flush=True)

    main_refl = runs["main-refl"]
    regions = {"all": np.ones_like(smooth), **reflective}
    result["reflections"] = {"main": score_run(main_refl, main_refl.truth, regions)}
    for label in [name for name in runs if name.startswith("B")]:
        run = runs[label]
        result["reflections"][label] = score_run(run, main_refl.truth, regions)
        if label == "B":
            keep("B-reflections", run, main_refl.truth)
    keep("main-reflections", main_refl, main_refl.truth)
    print("reflections done", flush=True)
    (out / f"{case}.json").write_text(json.dumps(result, indent=1))


main()
