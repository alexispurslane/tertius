"""Reads the scratch profile of the film run and prints the per-section tables for t88-hjjm.
Usage: python3 -I analyze.py <profile.json> [<profile.ticks.csv>]"""
import csv
import json
import sys

d = json.load(open(sys.argv[1]))
S = d["sections"]
print("processors", d["processorCount"], "server GC", d["gcServer"], "SyncProfile compiled", d["syncProfileCompiled"])


def span(s, name):
    return s["spans"][name]["ms"]


T, GP, PW, SY, PR, FL = ("tick (World.Tick)", "  gameplay", "  wait for physics after gameplay", "  sync point",
                         "  prepare", "  flush log")
PS = "physics step (physics thread)"
CAP, SINK, RO, RT = "capture (playback.Capture)", "  video sink write", "readout row", "runner ticks (RunTicks, incl. WaitForTick)"
PHYS = ["  reconcile and refresh frames", "  hulls, forms, spawn and static hulls", "  intents and gravity",
        "  update characters", "  update systems (Jolt)", "  snap kinematic, proper accelerations", "  collect contacts",
        "  hand off bodies", "  ghosts and ended contacts", "  write bodies and frames", "  physics gate wait"]

print("\n## Per section: wall, ticks and frames")
print("| section | every | frames | ticks | wall s | tick ms/tick | p50 | p95 | max | capture ms/frame | sink ms/frame | other ms | CPU/wall |")
print("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
for s in S:
    t = s["ticks"] or 1
    f = s["frames"] or 1
    other = s["wallMs"] - span(s, RT) - span(s, CAP) - span(s, RO)
    print(f"| {s['section']} | {s['every']} | {s['frames']} | {s['ticks']} | {s['wallMs']/1000:.1f} | "
          f"{span(s, T)/t:.2f} | {s['tickMsP50']:.2f} | {s['tickMsP95']:.2f} | {s['tickMsMax']:.1f} | "
          f"{(span(s, CAP)-span(s, SINK))/f:.2f} | {span(s, SINK)/f:.2f} | {other:.0f} | {s['cpuMs']/max(1, s['wallMs']):.2f} |")

focus = sys.argv[2:] or ["glide-window", "ring-approach", "glide", "arrival", "shaft", "ride-side", "ride", "foot", "transfer", "entry"]
byname = {s["section"]: s for s in S}
print("\n## Tick split, ms per tick")
print("| section | tick | prepare | gameplay | wait for physics | sync | physics step (thread) | Jolt update | characters | contacts | hand-offs | ghosts | Jolt systems/tick | active bodies/update | chars/tick | slowest Jolt update |")
print("|---|" + "---:|" * 14 + "---|")
for n in focus:
    s = byname.get(n)
    if not s:
        continue
    t = s["ticks"] or 1
    up = s["joltSystemsUpdated"] or 1
    print(f"| {n} | {span(s, T)/t:.2f} | {span(s, PR)/t:.2f} | {span(s, GP)/t:.2f} | {span(s, PW)/t:.2f} | {span(s, SY)/t:.2f} | "
          f"{span(s, PS)/t:.2f} | {span(s, '  update systems (Jolt)')/t:.2f} | {span(s, '  update characters')/t:.2f} | "
          f"{span(s, '  collect contacts')/t:.2f} | {span(s, '  hand off bodies')/t:.2f} | {span(s, '  ghosts and ended contacts')/t:.2f} | "
          f"{s['joltSystemsUpdated']/t:.2f} | {s['joltActiveRigidBodiesSum']/up:.1f} | {s['charactersUpdated']/t:.2f} | "
          f"{s['slowestJoltUpdateMs']:.1f} ms {s['slowestJoltUpdate']} |")

print("\n## Sync point steps, ms per tick (top 6 per section)")
for n in focus:
    s = byname.get(n)
    if not s:
        continue
    t = s["ticks"] or 1
    top = sorted(s["syncSteps"].items(), key=lambda kv: -kv[1])[:6]
    print(f"- {n}: " + "; ".join(f"{k.strip()} {v/t:.2f}" for k, v in top))

print("\n## Physics phases, ms per tick")
for n in focus:
    s = byname.get(n)
    if not s:
        continue
    t = s["ticks"] or 1
    print(f"- {n}: " + "; ".join(f"{p.strip()} {span(s, p)/t:.3f}" for p in PHYS))

print("\n## GC per section (focus)")
for n in focus:
    s = byname.get(n)
    if s:
        print(f"- {n}: gen0 {s['gcGen0']} gen1 {s['gcGen1']} gen2 {s['gcGen2']}, pause {s['gcPauseMs']:.1f} ms, "
              f"allocated {s['allocatedMB']:.1f} MB over {s['ticks']} ticks")

tot = {k: sum(span(s, k) for s in S) for k in (T, CAP, SINK, RT, RO)}
wall = sum(s["wallMs"] for s in S)
print(f"\nwhole film: wall {wall/1000:.1f} s; ticks {sum(s['ticks'] for s in S)}; tick {tot[T]/1000:.1f} s; "
      f"capture {tot[CAP]/1000:.1f} s of which sink write {tot[SINK]/1000:.1f} s; readouts {tot[RO]/1000:.1f} s")
