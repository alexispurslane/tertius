#!/usr/bin/env python3
"""Throwaway (never merges): a firefly-robust long reference of the survey's rect-lamps still view, and the preview's
columns re-rendered beside it in one job.

  robust.py render <R> <t0 file> <stop at s after t0>  main=<bin>[:ENV=V,...] ...
      1. Each named column renders the still view with 8 continued captures (draws 33 ... 264) and keeps captures 3
         and 7 (draws 132 and 264) as R/s/<name>-<draws>/images/still/rect-lamps.png (daub.py's layout).
      2. One long run of bin-x with both clamps off (TERTIUS_A_NO_CLAMP=1) renders up to MAX_CAPTURES continued
         captures and stops at the time limit. Its captures 3 and 7 are the truth column. From capture SKIP on, each
         BLOCK captures sum to one block mean, read while the demo still renders.
      3. References, each saved as an 8-bit gray sRGB image as stills.py saves one:
           truth     = the mean of captures 9 ... 28, the old long reference, rebuilt on this worker
           plainlong = the plain mean of every complete block
           mom<B>    = the per-pixel median of batch means of B captures (B = 10, 20, 40)
      4. R/bias.json: for each estimator, the regional means against the plain mean of the same captures, and a
         firefly rate per region; R/pack: lossless WebP crops.
  robust.py specks <R> <ref>: the specks of every column against s/<ref>'s reference, into R/specks-<ref>.json.
"""
import importlib.util, json, os, shutil, signal, subprocess, sys, threading, time
from pathlib import Path

T = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("survey", T / "tools" / "renderer-survey.py")
sv = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sv)
n = sv.np()
from PIL import Image

CASE = "rect-lamps"
KEEP = (3, 7)
COLUMN_CAPTURES = 8
SKIP = 9            # the old reference's first capture: draw 330 of a first still of 33 draws
BLOCK = 10
BATCHES = (10, 20, 40)
MAX_CAPTURES = 1200
CEILING = (480, 0, 1440, 440)
CLOUD = (760, 120, 1160, 420)

case = next(c for c in sv.load_cases()["stills"] if c["name"] == CASE)
source, frame = sv.source_of(case)


def view(count):
    built, still = sv.views_script(source, {frame: sv.captures("v", count)})
    return built, still[frame]


def save_srgb(y, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    srgb = n.where(y <= 0.0031308, y * 12.92, 1.055 * n.power(n.maximum(y, 0), 1 / 2.4) - 0.055)
    Image.fromarray((n.clip(srgb, 0, 1) * 255 + 0.5).astype(n.uint8)).save(path)


def command(binary, out, script):
    out.mkdir(parents=True, exist_ok=True)
    path = out / "script.json"
    path.write_text(json.dumps(script, indent=1))
    return [str(binary), "--script", str(path), "--headless", "--out", str(out), "--size", sv.SIZE, *sv.NO_BLOOM]


def parse(arg):
    name, rest = arg.split("=", 1)
    binary, _, envs = rest.partition(":")
    return name, binary, dict(e.split("=", 1) for e in envs.split(",") if e)


def keep(R, name, k, first, src):
    dst = R / "s" / f"{name}-{sv.still_draws(first, k)}" / "images" / "still" / f"{CASE}.png"
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(src, dst)


def render(R, t0, stop, columns):
    timing = {}
    script, first = view(COLUMN_CAPTURES)
    for arg in columns:
        name, binary, env = parse(arg)
        start = time.time()
        work = R / "work" / name
        with open(R / f"{name}.log", "w") as log:
            subprocess.run(command(R / binary / "Tertius.Demo", work, script), cwd=T, check=True, stdout=log,
                           stderr=subprocess.STDOUT, env={**os.environ, **sv.STILL_CONTINUES, **env})
        for k in KEEP:
            keep(R, name, k, first, work / f"v-s{k:02d}.png")
        shutil.rmtree(work)
        timing[name] = time.time() - start
        print(name, f"{timing[name]:.0f} s", flush=True)

    # The long run, with both clamps off, stopped at the time limit.
    script, first = view(MAX_CAPTURES)
    work = R / "work" / "long"
    env = {**os.environ, **sv.STILL_CONTINUES, "TERTIUS_A_NO_CLAMP": "1"}
    start = time.time()
    log = open(R / "long.log", "w")
    proc = subprocess.Popen(command(R / "bin-x" / "Tertius.Demo", work, script), cwd=T, stdout=log,
                            stderr=subprocess.STDOUT, env=env)
    blocks, current, inblock = [], None, 0
    k = 0
    seen = {}

    def ready(k):
        # A capture is complete once the demo has written the one after the next, or has ended.
        return (work / f"v-s{k + 2:02d}.png").exists()

    while True:
        if time.time() >= t0 + stop and proc.poll() is None:
            proc.send_signal(signal.SIGTERM)
            try:
                proc.wait(10)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()
        path = work / f"v-s{k:02d}.png"
        if ready(k):
            if k in KEEP:
                keep(R, "truth", k, first, path)
            if k >= SKIP:
                for attempt in range(20):
                    try:
                        y = sv.luminance(path)
                        break
                    except OSError:
                        time.sleep(0.5)  # still being written
                else:
                    raise RuntimeError(f"{path} stays unreadable")
                current = y.astype(n.float32) if current is None else current + y
                inblock += 1
                if inblock == BLOCK:
                    blocks.append(current / BLOCK)
                    current, inblock = None, 0
            path.unlink()
            k += 1
            continue
        if proc.poll() is not None:
            break  # the last one or two captures may be partial: drop them
        time.sleep(0.2)
    log.close()
    timing["long"] = time.time() - start
    timing["longCaptures"] = k
    print(f"long: {k} captures read, {len(blocks)} blocks in {timing['long']:.0f} s", flush=True)
    shutil.rmtree(work, ignore_errors=True)

    stack = n.stack(blocks)                                  # (blocks, H, W), each the mean of BLOCK captures
    refs = {"truth": stack[0:2].mean(0), "plainlong": stack.mean(0)}
    same_plain = {}
    for B in BATCHES:
        per = B // BLOCK
        K = len(blocks) // per
        batches = stack[:K * per].reshape(K, per, *stack.shape[1:]).mean(1)
        refs[f"mom{B}"] = n.median(batches, axis=0)
        same_plain[f"mom{B}"] = (batches.mean(0), K)
        if B == 20:
            # The cloud crop of every batch mean, to see how often a pixel holds a firefly.
            for i in range(K):
                save_crop(batches[i], R / "pack" / f"batch20-{i:02d}-cloud.webp", CLOUD)
    for name, y in refs.items():
        save_srgb(y, R / "s" / name / "images" / "still" / f"{CASE}-reference.png")

    bias = {"blocks": len(blocks), "block": BLOCK, "skip": SKIP, "capturesUsed": len(blocks) * BLOCK,
            "firstDraw": sv.still_draws(first, SKIP), "lastDraw": sv.still_draws(first, SKIP + len(blocks) * BLOCK - 1),
            "timing": timing, "estimators": {}}
    sys.path.insert(0, str(T / "tools" / "preview"))
    from light import REGIONS, mask as region_mask, smooth_of
    plain = refs["plainlong"]
    smooth = smooth_of(plain)
    keep_px = sv.visible(plain) & ~sv.near_emitters(plain)
    regions = {f"{name} (smooth)": smooth & region_mask(polys) for name, polys in REGIONS[CASE].items()}
    h, w = plain.shape
    for r in range(3):
        for c in range(3):
            m = n.zeros_like(keep_px)
            m[r * h // 3:(r + 1) * h // 3, c * w // 3:(c + 1) * w // 3] = True
            regions[f"r{r}{c}"] = m & keep_px
    regions["whole (visible, no emitters)"] = keep_px
    # Firefly rate: speck pixels of single 10-capture block means, per 10,000 pixels of the region (mean of 4 blocks).
    picks = n.linspace(0, len(blocks) - 1, 4).astype(int)
    rate = sum(sv.speck_mask(stack[i]).astype(n.float32) for i in picks) / len(picks)
    bias["fireflyPer10k"] = {name: float(1e4 * rate[m].mean()) for name, m in regions.items()}
    for name, (mean_same, K) in same_plain.items():
        y = refs[name]
        bias["estimators"][name] = {"K": K, "B": int(name[3:]), "biasPercent": {
            region: float(100 * (y[m].mean() / mean_same[m].mean() - 1)) for region, m in regions.items()}}
    bias["estimators"]["truth (old)"] = {"vsPlainlongPercent": {
        region: float(100 * (refs["truth"][m].mean() / plain[m].mean() - 1)) for region, m in regions.items()}}
    (R / "bias.json").write_text(json.dumps(bias, indent=1))

    pack = R / "pack"
    for name in refs:
        im = Image.open(R / "s" / name / "images" / "still" / f"{CASE}-reference.png").convert("L")
        im.crop(CEILING).save(pack / f"{name}-ceiling.webp", lossless=True, method=6)
    for name in ("mom20", "plainlong"):
        Image.open(R / "s" / name / "images" / "still" / f"{CASE}-reference.png").convert("L").save(
            pack / f"{name}-full.webp", lossless=True, method=6)
    for d in sorted((R / "s").iterdir()):
        still = d / "images" / "still" / f"{CASE}.png"
        if still.exists() and d.name.endswith("-264"):
            Image.open(still).convert("RGB").crop(CEILING).save(pack / f"{d.name}-ceiling.webp", lossless=True, method=6)
    print("pack", sum(p.stat().st_size for p in pack.iterdir()), flush=True)


def save_crop(y, path, box):
    path.parent.mkdir(parents=True, exist_ok=True)
    srgb = n.where(y <= 0.0031308, y * 12.92, 1.055 * n.power(n.maximum(y, 0), 1 / 2.4) - 0.055)
    Image.fromarray((n.clip(srgb, 0, 1) * 255 + 0.5).astype(n.uint8)).crop(box).save(path, lossless=True, method=6)


def specks(R, ref):
    reference = sv.luminance(R / "s" / ref / "images" / "still" / f"{CASE}-reference.png")
    out = {}
    for d in sorted((R / "s").iterdir()):
        still = d / "images" / "still" / f"{CASE}.png"
        if still.exists():
            out[d.name] = sv.specks(sv.luminance(still), reference)
    (R / f"specks-{ref}.json").write_text(json.dumps(out, indent=1))


if __name__ == "__main__":
    what, R = sys.argv[1], Path(sys.argv[2]).resolve()
    if what == "render":
        t0 = float(Path(sys.argv[3]).read_text())
        render(R, t0, float(sys.argv[4]), sys.argv[5:])
    else:
        specks(R, sys.argv[3])
