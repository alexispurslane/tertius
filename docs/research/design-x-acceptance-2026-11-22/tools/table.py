# Tool code of t0-acxm, copied unchanged for t0-5tyj. table.py <timing folder> <base side> <side> [round...]: the GPU frame span of each run (the
# median over the run's frames, gpu-timings.json), the paired difference side − base per round, and the median and the
# spread (the highest minus the lowest) of the differences, for the deck and the film. Rounds default to every round
# that ran both sides. The load is the machine's load over one minute before the base's run and the side's run, from the
# round's log (round-inner.sh).
import json, re, statistics, sys
from pathlib import Path

folder, base, side = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
wanted = sys.argv[4:]


def span(run):
    data = json.loads((run / "gpu-timings.json").read_text())
    clock = data.get("gpuClock", {})
    return data["frame"]["medianMs"], clock.get("meanMhz", "unknown")


def load(r, side, script):
    log = folder / f"{r}.log"
    if not log.exists():
        return "?"
    for line in log.read_text().splitlines():
        found = re.match(rf"\S+ {re.escape(side)} {script} .*load averages?: ([0-9.]+)", line)
        if found:
            return found.group(1)
    return "?"


for script in ("deck", "film"):
    rounds = sorted({p.name.split("-")[0] for p in folder.glob(f"*-{base}-{script}")})
    rounds = [r for r in rounds if (not wanted or r in wanted) and (folder / f"{r}-{side}-{script}" / "gpu-timings.json").exists()
              and (folder / f"{r}-{base}-{script}" / "gpu-timings.json").exists()]
    differences = []
    print(f"{script}: round, {base} ms, {side} ms, difference, clocks, loads")
    for r in rounds:
        (b, bc), (s, sc) = span(folder / f"{r}-{base}-{script}"), span(folder / f"{r}-{side}-{script}")
        differences.append(s - b)
        print(f"  {r}  {b:.3f}  {s:.3f}  {s - b:+.3f}  {bc} / {sc}  {load(r, base, script)} / {load(r, side, script)}")
    if differences:
        print(f"  median difference {statistics.median(differences):+.3f} ms, spread {max(differences) - min(differences):.3f} ms, n {len(differences)}")
