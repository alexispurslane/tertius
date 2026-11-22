# Tool code of t0-5tyj. switchtable.py <label=run folder>...: the survey's switch results of each run, one row per case
# and run. offDraws / onDraws: the draws until the slowest changed region of the 3 x 3 grid settles after the lamps or
# the sun turn off / on (within offWindow / onWindow draws). offDrawsWhole / onDrawsWhole: the same for the whole image.
# change: the whole image's change of light at the switch. failed: the survey's failed measures.
import json
import sys
from pathlib import Path

print(f"{'case':14s} {'run':10s} {'off':>5s} {'on':>5s} {'off whole':>10s} {'on whole':>9s} {'change':>8s}  failed")
for arg in sys.argv[1:]:
    label, folder = arg.split('=', 1)
    for p in sorted(Path(folder, 'results', 'switch').glob('*.json')):
        r = json.loads(p.read_text())
        print(f"{p.stem:14s} {label:10s} {r['offDraws']:5d} {r['onDraws']:5d} {r['offDrawsWhole']:10d} {r['onDrawsWhole']:9d} "
              f"{r['change']:+8.4f}  {','.join(r['failed']) or '-'}")
