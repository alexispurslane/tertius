#!/usr/bin/env python3
"""Throwaway (t1-yy2f, part 3): the real hits on sheets that a sheet rule loses, by reason.

Reads the records.bin of a census run with TERTIUS_PROBE_SHEET_RULES=1 (screen_trace_resolve.comp, probeRecord): one
record per counted ray that hits a sheet under the frame's own slab, the 10 % rule. Each record holds 18 words: the draw,
the starting pixel x | y << 16, and the 16 words of probeRecord. Prints the tables of cards.md, part 3.

    python3 -I sheet_rules.py <census folder> [<census folder> ...]
"""
import sys
from pathlib import Path

import numpy as np

RULES = {1: "no thickness", 2: "plane span, a = 0.5", 3: "plane span, a = 1.5", 4: "plane bounds, both sides"}
REASONS = {
    1: "1 the stair case: the crossing inside the hit pixel",
    2: "2 the crossing one pixel before the hit pixel",
    3: "3 the crossing two or more pixels before the hit pixel",
    4: "4 the crossing shows another surface than the hit pixel's card",
    5: "5 other: the crossing off screen",
}


def load(folder):
    words = np.fromfile(Path(folder) / "records.bin", dtype="<u4").reshape(-1, 18)
    f = words[:, 2:].view(np.float32)
    w0 = words[:, 2]
    r = {
        "draw": words[:, 0],
        "start": words[:, 1],
        "crossing": (w0 >> 1) & 7,
        "reason": (w0 >> 4) & 3,
        "behind": (w0 >> 6) & 1,
        "own": (w0 >> 7) & 1,
        "diffuse": (w0 >> 8) & 1,
        "on_card": (w0 >> 9) & 1,
        "placed": (w0 >> 10) & 1,
        "hx": (words[:, 3] & 0xFFFF).astype(np.float64),
        "hy": (words[:, 3] >> 16).astype(np.float64),
        "px": f[:, 2].astype(np.float64),
        "py": f[:, 3].astype(np.float64),
        "w": f[:, 4].astype(np.float64),
        "in_w": f[:, 5].astype(np.float64),
        "out_w": f[:, 6].astype(np.float64),
        "span": f[:, 7].astype(np.float64),
        "far": f[:, 8].astype(np.float64),
        "sine": f[:, 9].astype(np.float64),
        "light": f[:, 10].astype(np.float64),
        "t": f[:, 11].astype(np.float64),
        "card_h": words[:, 14] & 0x3FFF,
        "card_p": (words[:, 14] >> 16) & 0x3FFF,
        "gx": f[:, 13].astype(np.float64),
        "gy": f[:, 14].astype(np.float64),
        "distance": f[:, 15].astype(np.float64),
    }
    for rule in RULES:
        r[f"outcome{rule}"] = (w0 >> (12 + 4 * rule)) & 15
    return r


def kept(r, rule):
    """A rule keeps a real hit when its walk again ends in a hit of class 1, on any pixel."""
    return (r[f"outcome{rule}"] & 7) == 5


def reasons(r):
    """The reason of each record, by the first that applies: 4, then 5, then 1 to 3 by the crossing's distance."""
    cheb = np.maximum(np.abs(np.floor(r["px"]) - r["hx"]), np.abs(np.floor(r["py"]) - r["hy"]))
    reason = np.where(cheb == 0, 1, np.where(cheb == 1, 2, 3))
    reason = np.where(r["placed"] == 0, 5, reason)
    other = (r["card_p"] != r["card_h"]) & (r["placed"] == 1)
    return np.where(other, 4, reason), cheb


def pct(a, b):
    return f"{100.0 * a / b:.1f} %" if b else "n/a"


def report(folder):
    r = load(folder)
    reason, cheb = reasons(r)
    depth = (r["w"] - np.maximum(r["in_w"], r["out_w"])) / np.maximum(0.5 * r["span"], 1e-30)
    angle = np.degrees(np.arcsin(np.clip(r["sine"], 0, 1)))
    # How far behind the hit pixel's center the ray's nearest point in the hit cell lies, along the view axis, in mm.
    depth_mm = (1.0 / np.maximum(np.maximum(r["in_w"], r["out_w"]), 1e-30) - 1.0 / r["w"]) * 1000.0
    print(f"## {folder}")
    print(f"records (hits on sheets under the 10 % rule): {len(reason)}")
    for kind, name in ((0, "reflection"), (1, "diffuse")):
        k = r["diffuse"] == kind
        real = k & (r["crossing"] == 1)
        print(f"\n### {name}: hits on sheets {k.sum()}, real (class 1) {real.sum()}")
        print("| Class of the 10 % rule's hit | Hits | " + " | ".join(f"Kept by {n}" for n in RULES.values()) + " |")
        print("|---|---:|" + "---:|" * len(RULES))
        for c, cname in ((1, "1 real"), (0, "0 an opening"), (2, "2 hidden"), (3, "3 undetermined")):
            m = k & (r["crossing"] == c)
            print(f"| {cname} | {m.sum()} | " + " | ".join(f"{(m & kept(r, x)).sum() if c == 1 else ((r[f'outcome{x}'] >= 4) & m).sum()}" for x in RULES) + " |")
        lost = real & ~kept(r, 1)
        light = r["light"][lost].sum()
        print(f"\nReal hits that no thickness loses: {lost.sum()} ({pct(lost.sum(), real.sum())}), light {light:.4g}")
        visible = real & (r["placed"] == 1) & (r["card_p"] == r["card_h"])
        other = real & ~visible
        print("\n| Real hits of the 10 % rule | Hits | " + " | ".join(f"Kept by {n}" for n in RULES.values()) + " |")
        print("|---|---:|" + "---:|" * len(RULES))
        for name, m in (("the crossing shows the hit pixel's card", visible), ("the crossing shows another surface", other)):
            print(f"| {name} | {m.sum()} | " + " | ".join(f"{(m & kept(r, x)).sum()} ({pct((m & kept(r, x)).sum(), m.sum())})" for x in RULES) + " |")
        print("\n| Reason | Lost hits | Share | Share of light | Depth into the slab, 25/50/75 % (spans) | Depth, 50 % (mm) | Angle to the plane, 50 % (deg) | Start on a card | " + " | ".join(f"Kept by {n}" for n in list(RULES.values())[1:]) + " |")
        print("|---|---:|---:|---:|---|---:|---:|---:|" + "---:|" * (len(RULES) - 1))
        for k_reason, rname in REASONS.items():
            m = lost & (reason == k_reason)
            if not m.any():
                print(f"| {rname} | 0 |" + " |" * (6 + len(RULES)))
                continue
            d = np.percentile(depth[m], [25, 50, 75])
            print(
                f"| {rname} | {m.sum()} | {pct(m.sum(), lost.sum())} | {pct(r['light'][m].sum(), light)} | "
                f"{d[0]:.3g} / {d[1]:.3g} / {d[2]:.3g} | {np.median(depth_mm[m]):.3g} | {np.median(angle[m]):.3g} | {pct(r['on_card'][m].sum(), m.sum())} | "
                + " | ".join(f"{(m & kept(r, x)).sum()}" for x in list(RULES)[1:])
                + " |"
            )
        # Checks of the census's own consistency.
        s1 = lost & (reason == 1)
        if s1.any():
            plane_at = r["w"] + r["gx"] * (r["px"] - r["hx"] - 0.5) + r["gy"] * (r["py"] - r["hy"] - 0.5)
            print(f"\ncheck: stair-case crossings where the plane is farther than the center: {(plane_at[s1] < r['w'][s1]).sum()} of {s1.sum()}")
        behind_center = real & (np.maximum(r["in_w"], r["out_w"]) < r["w"])
        print(f"check: real hits whose ray stays behind the center w across the hit cell: {behind_center.sum()}, of them lost by no thickness: {(behind_center & lost).sum()}; lost hits not behind it: {(lost & ~behind_center).sum()}")
        for x in (2, 3):
            pred = real & (depth <= (1 if x == 2 else 3))
            print(f"check: {RULES[x]}: real hits with depth <= {1 if x == 2 else 3} spans {pred.sum()}, kept {(real & kept(r, x)).sum()}, kept and not so deep {(real & kept(r, x) & ~pred).sum()}")
        print(f"cheb distance of lost hits: " + ", ".join(f"{d}: {(lost & (cheb == d)).sum()}" for d in range(0, 4)) + f", 4+: {(lost & (cheb >= 4)).sum()}")
        same = lost & (reason != 4) & (reason != 5)
        print(f"lost real hits whose crossing shows the hit pixel's own card: {same.sum()} ({pct(same.sum(), lost.sum())}), light {pct(r['light'][same].sum(), light)}")


if __name__ == "__main__":
    for folder in sys.argv[1:]:
        report(folder)
