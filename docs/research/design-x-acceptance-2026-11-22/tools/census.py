# Tool code of t0-5tyj. census.py <census run folder>...: take 6, the evidence census of the four stills on X (acceptance
# point 12) beside the region test's fires in the same render. Per still and signal, from the survey's census-still
# result: the block-draws (8 x 8 output-pixel blocks times draws), and the shares with motion evidence and with light
# evidence (levelShares). Per still, from the region log of the same render (TERTIUS_X_REGION_LOG, region-none.txt, one
# line per draw "<draw> <draw index> <anchors> <tested> <fires> <row>"): the share of anchor-draws that the test ran on,
# and the share of tested anchor-draws where it fired, over the draws from the first tested draw to the end.
import json
import sys
from pathlib import Path

LOGS = Path('/private/tmp/t0-5tyj/regionlog')
print(f"{'still':14s} {'signal':9s} {'draws':>6s} {'block-draws':>12s} {'motion':>10s} {'light':>10s}   "
      f"{'region draws':>12s} {'tested':>8s} {'fires':>8s}")
for folder in map(Path, sys.argv[1:]):
    for path in sorted((folder / 'results' / 'census-still').glob('*.json')):
        result = json.loads(path.read_text())
        rows = [list(map(int, l.split())) for l in (LOGS / path.stem / 'region-none.txt').read_text().splitlines()]
        tested_rows = [r for r in rows if r[3] > 0]
        cells = sum(r[2] for r in rows[rows.index(tested_rows[0]):]) if tested_rows else 0
        tested = sum(r[3] for r in tested_rows)
        fires = sum(r[4] for r in tested_rows)
        region = (f"{tested_rows[0][0] if tested_rows else '-':>5}-{rows[-1][0]:<6} {100 * tested / cells:7.1f}% "
                  f"{100 * fires / tested:7.2f}%") if tested else f"{'none tested':>30s}"
        for signal in result['signals']:
            shares = signal['levelShares']
            print(f"{path.stem:14s} {signal['light']:9s} {signal['draws']:6d} {signal['blockDraws']:12d} "
                  f"{shares['motion']:10.2e} {shares['light']:10.2e}   {region}")
