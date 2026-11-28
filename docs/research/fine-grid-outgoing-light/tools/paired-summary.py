"""Summarize paired deck runs while excluding the timestamp interval for headless capture."""
import argparse
import json
from pathlib import Path
from statistics import median


def timings(folder, variant, run):
    report = json.loads((folder/f"out/{variant}/run-{run}/gpu-timings.json").read_text())
    assert (report["width"], report["height"]) == (1920, 1080), (variant, run)
    assert "RTX 2000 Ada" in report["device"] and not report["stampsLatchLate"]
    assert report["passes"][0]["pass"] == "deformation"
    assert [p["pass"] for p in report["passes"][-2:]] == ["outlines", "capture"]
    span = next(r["timing"] for r in report["runs"] if (r["first"], r["last"]) == ("deformation", "outlines"))
    volume = next(p for p in report["passes"] if p["pass"] == "directional light volume")
    assert span["frames"] == volume["frames"] == report["frame"]["frames"] == 120
    return span["medianMs"], span["p95Ms"], volume["medianMs"], volume["p95Ms"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    args = parser.parse_args()
    names = ("GPU pass sum median", "GPU pass sum p95", "Volume median", "Volume p95")
    observations = [[timings(args.folder, name, run) for run in range(1,6)] for name in ("main","fix")]
    print("| Time (ms) | Main median across runs | Fix median across runs | Median paired change | Range of paired changes |")
    print("|---|---:|---:|---:|---:|")
    for metric, name in enumerate(names):
        base, candidate = ([run[metric] for run in variant] for variant in observations)
        delta = [cand-before for cand,before in zip(candidate,base)]
        print(f"| {name} | {median(base):.3f} | {median(candidate):.3f} | {median(delta):+.3f} | {min(delta):+.3f} to {max(delta):+.3f} |")
    print()
    print("| Pair | Main median | Fix median | Main p95 | Fix p95 | Main volume median | Fix volume median |")
    print("|---:|---:|---:|---:|---:|---:|---:|")
    for pair, (base,candidate) in enumerate(zip(*observations),1):
        print(f"| {pair} | {base[0]:.3f} | {candidate[0]:.3f} | {base[1]:.3f} | {candidate[1]:.3f} | {base[2]:.3f} | {candidate[2]:.3f} |")


if __name__ == "__main__":
    main()
