# Tool code of t0-gbgj. summary.py: the survey's numbers that the record's tables cite, from the runs in $W/runs (m.sh
# runs it in $W/w, where s/<run> links each run). Stills: specks (per 10,000 visible pixels), blotch and level of each
# case. Switch: r11 (series index 5) over frames 140 to 159 against the side's own lamps-on control and against M2's.
# Films rect-bay and bay-sunbeam: the survey's flicker, grain flicker, the exposure and the side's own exposure. Lag: the median and largest lag
# and lag error.
import json, os
def load(p):
    return json.load(open(p)) if os.path.exists(p) else None
STILLS = ['rect-lamps', 'exit-cabin', 'docking-cabin', 'cabin-light-cabin', 'deck-cuts', 'bay-sunbeam']
print('# Stills: specks / blotch / level')
for run in ['s-M2', 's-X2', 's-A2', 'e-M2', 'e-X2', 'e-A2']:
    for c in STILLS:
        r = load(f's/{run}/results/still/{c}.json')
        if r: print(f"{run:6s} {c:18s} specks {r['specks']:.4f} blotch {r['blotch']:.4f} level {r['level']:+.4f} failed {r['failed']}")
print('# Switch cabin: r11 over frames 140 to 159, lowest / highest, in % of a lamps-on control')
ref = load('s/w-M2/results/switch/cabin.json')
for run in ['w-M2', 'w-X2']:
    r = load(f's/{run}/results/switch/cabin.json')
    if not r: continue
    s = r['series']
    for name, ctl in (('own', s['on']), ('M2', ref['series']['on'])):
        q = [100 * s['switch'][t][5] / ctl[t][5] for t in range(140, 160)]
        print(f"{run} against {name:3s} control: r11 lowest {min(q):.1f} % highest {max(q):.1f} %")
    print(f"{run} survey: slowest off/on {r['offDraws']} / {r['onDraws']}, whole {r['offDrawsWhole']} / {r['onDrawsWhole']}, failed {r['failed']}")
for case, runs in (('rect-bay', ['f-M2', 'f-X2']), ('bay-sunbeam', ['fs-M2', 'fs-X2'])):
  print(f'# Film {case}')
  for run in runs:
    r = load(f's/{run}/results/motion/{case}.json')
    if r: print(f"{run} stops {r['stops']:.4f} own {r['ownStops']:.4f} frames {r['frames']} flicker {r['flicker']:.5f} grain {r['grainFlicker']:.5f} failed {r['failed']}")
print('# Lag bay-sun')
for run in ['l-M2', 'l-X2']:
    r = load(f's/{run}/results/lag/bay-sun.json')
    if r: print(f"{run} median lag {r['lagDraws']:.2f} draws, largest {r['lagDrawsMax']:.2f}, median error {r['errorMedian']:.4f}, largest error {r['errorMax']:.4f}")
