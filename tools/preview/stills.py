#!/usr/bin/env python3
"""Throwaway (never merges): the rect-lamps still view of the survey's still part for several variants in one job, with
the captures at 132 and 264 draws kept (v-s03 and v-s07 of the continued stills), each scored like run_still against
its own long reference, and its specks also against the truth's long reference. Writes R/s/<variant>-<draws> and
R/s/<variant> in daub.py's layout, R/scores.json, and lossless WebP views in R/pack.
Usage: stills.py <R> <name>=<bin>[:ENV=VALUE,...] ..."""
import importlib.util, json, os, shutil, sys, traceback
from pathlib import Path

R = Path(sys.argv[1]).resolve()
T = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("survey", T / "tools" / "renderer-survey.py")
sv = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sv)
os.environ[sv.HOLDS_LOCK] = "1"
CASE = "rect-lamps"
KEEP = (3, 7)
case = next(c for c in sv.load_cases()["stills"] if c["name"] == CASE)
script, first = sv.still_script(*sv.source_of(case))
n = sv.np()
from PIL import Image

def save_srgb(y, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    srgb = n.where(y <= 0.0031308, y * 12.92, 1.055 * n.power(y, 1 / 2.4) - 0.055)
    Image.fromarray((n.clip(srgb, 0, 1) * 255 + 0.5).astype(n.uint8)).save(path)

scores = {}
for arg in sys.argv[2:]:
    name, rest = arg.split("=", 1)
    binary, _, envs = rest.partition(":")
    env = dict(e.split("=", 1) for e in envs.split(",") if e)
    work = R / "work" / name
    try:
        sv.render(work, script, binary=R / binary / "Tertius.Demo", env={**sv.STILL_CONTINUES, **env}, extra=sv.NO_BLOOM)
    except Exception:
        traceback.print_exc()
        continue
    ref = None
    for k in range(sv.STILL_CAPTURES - sv.REFERENCE_CAPTURES, sv.STILL_CAPTURES):
        y = sv.luminance(work / f"v-s{k:02d}.png")
        ref = y if ref is None else ref + y
    ref /= sv.REFERENCE_CAPTURES
    save_srgb(ref, R / "s" / name / "images" / "still" / f"{CASE}-reference.png")
    for k in KEEP:
        draws = sv.still_draws(first, k)
        shown = sv.luminance(work / f"v-s{k:02d}.png")
        score, rms = sv.blotch(shown, ref)
        scores[f"{name}-{draws}"] = {"env": env, "draws": draws, "specksOwnReference": sv.specks(shown, ref),
                                     "blotch": score, "blotchRms": rms, "level": sv.level(shown, ref)}
        dst = R / "s" / f"{name}-{draws}" / "images" / "still" / f"{CASE}.png"
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(work / f"v-s{k:02d}.png", dst)
    shutil.rmtree(work)
    print(name, "done", flush=True)

truth = sv.luminance(R / "s" / "truth" / "images" / "still" / f"{CASE}-reference.png")
for key, row in scores.items():
    row["specksTruthReference"] = sv.specks(sv.luminance(R / "s" / key / "images" / "still" / f"{CASE}.png"), truth)
(R / "scores.json").write_text(json.dumps(scores, indent=1))

# Lossless WebP: the ceiling strip at full resolution and the full view at half resolution.
pack = R / "pack"
pack.mkdir(exist_ok=True)
images = {"truth-reference": R / "s" / "truth" / "images" / "still" / f"{CASE}-reference.png"}
images.update({key: R / "s" / key / "images" / "still" / f"{CASE}.png" for key in scores})
for key, path in images.items():
    im = Image.open(path).convert("RGB")
    if key == "truth-reference" or not key.startswith("truth-"):
        im.crop((480, 0, 1440, 440)).save(pack / f"{key}-ceiling.webp", lossless=True, method=6)
    if key == "truth-reference" or not key.startswith(("truth-", "nospec-")):
        im.resize((960, 540), Image.LANCZOS).save(pack / f"{key}-half.webp", lossless=True, method=6)
print("pack", sum(p.stat().st_size for p in pack.iterdir()))
