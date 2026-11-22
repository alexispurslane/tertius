#!/usr/bin/env python3
"""Throwaway (t1-stwp): the markdown rows of cost.md, from the same reading and rules as cost_report.py.

    python3 -I cost_table.py <cost folder>

<cost folder> holds mac/ (mac-take2.sh) and cloud/jobA, cloud/jobC (cloud-jobs.sh). A cell gives the mean, then the
lowest and the highest value. A difference pairs two sides inside one round. The kept rounds are those of
cost_report.py. Two marks: ‡ means that the per-round range holds 0, and † means that the mean difference lies inside
main's run range: its size is at most main's slowest run minus its fastest, over the same rounds.
"""

import statistics as st
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import cost_report as cr  # noqa: E402

SCENES = [("deck", "temporal-upscale-timing", "jobA"), ("cabin", "ter-kh4g-cabin-timing", "jobC"),
          ("rect", "ter-kh4g-rect-lamps-timing", "jobC"), ("crowd", "ter-kh4g-crowd-timing", "jobC")]
LABELS = {"deck": "Deck", "cabin": "Cabin", "rect": "`rect-lamps`", "crowd": "Crowd"}
DIFFS = cr.PAIRS


def side_cell(runs, rounds):
    v = [runs["main"][r]["span"] for r in rounds]
    return f"{st.mean(v):.3f} ({min(v):.3f} to {max(v):.3f}, spread {max(v) - min(v):.3f})"


def diff_cell(runs, rounds, a, b):
    v = [runs[a][r]["span"] - runs[b][r]["span"] for r in rounds]
    main = [runs["main"][r]["span"] for r in rounds]
    mark = (" ‡" if cr.holds_zero(v) else "") + (" †" if abs(st.mean(v)) <= max(main) - min(main) else "")
    return f"{st.mean(v):+.3f} ({min(v):+.3f} to {max(v):+.3f}){mark}"


def rows(runs, rounds, label):
    return "| " + " | ".join([label, str(len(rounds)), side_cell(runs, rounds)]
                             + [diff_cell(runs, rounds, a, b) for a, b in DIFFS]) + " |"


def passes(runs, rounds, label):
    def med(side, name):
        v = [runs[side][r]["passes"].get(name) for r in rounds]
        v = [x for x in v if x is not None]
        return st.median(v) if v else None
    cells = [label]
    cells.append(f"{med('layer', 'back faces'):.3f}")
    for name in ("surface test", "depth pyramid", "reflection rays", "diffuse rays"):
        m, o, t, l = (med(s, name) for s in ("main", "off", "ten", "layer"))
        cells.append(f"{m:.3f} / {o - m:+.3f} / {t - m:+.3f} / {l - m:+.3f}")
    return "| " + " | ".join(cells) + " |"


def main():
    root = Path(sys.argv[1])
    print("Mac")
    for key, scene, _ in SCENES:
        runs = cr.read_scene(root / "mac" / scene, cloud=False)
        kept, dropped = cr.complete_rounds(runs, check_clock=True)
        print(rows(runs, kept, LABELS[key]), "dropped", dropped)
    print("Cloud, all rounds")
    for key, _, job in SCENES:
        runs = cr.read_scene(root / "cloud" / job / f"out-{key}", cloud=True)
        kept, _ = cr.complete_rounds(runs, check_clock=False)
        print(rows(runs, kept, LABELS[key]))
    print("Cloud, without round 1")
    for key, _, job in SCENES:
        runs = cr.read_scene(root / "cloud" / job / f"out-{key}", cloud=True)
        kept, _ = cr.complete_rounds(runs, check_clock=False)
        print(rows(runs, kept[1:], LABELS[key]))
    print("Cloud passes, the median over all five runs: main / off / ten / layer minus main")
    for key, _, job in SCENES:
        runs = cr.read_scene(root / "cloud" / job / f"out-{key}", cloud=True)
        kept, _ = cr.complete_rounds(runs, check_clock=False)
        print(passes(runs, kept, LABELS[key]))


if __name__ == "__main__":
    main()
