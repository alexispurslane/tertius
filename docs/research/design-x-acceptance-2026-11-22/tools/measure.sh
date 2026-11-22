#!/usr/bin/env bash
# Tool code of t0-5tyj. measure.sh: prints every measure of the record (README.md) from the runs under $BASE (default
# /private/tmp/t0-5tyj), in the record's order: ../tables.txt is its output. Run it from the checkout's root:
#   bash docs/research/design-x-acceptance-2026-11-22/tools/measure.sh > docs/research/design-x-acceptance-2026-11-22/tables.txt
# A section whose runs are gone prints a line that says so. The raw frames of the fair switch on M were deleted after
# its video and numbers were committed, so M's boil comes from ticket tc-bece, and only the sides whose frames remain are
# measured again here.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd)
D=$(cd "$T/.." && pwd)
R=$(cd "$T/../../../.." && pwd)
B=${BASE:-/private/tmp/t0-5tyj}; RUNS=$B/runs
OLD=$R/docs/research/design-x-acceptance-2026-11-14/tools
section() { printf '\n== %s\n' "$1"; }

section "1. The stills (stilltable.py)"
python3 -I "$T/stilltable.py"

section "2. The light share at a fixed exposure (light.py of design-x-acceptance-2026-11-14) and the daub (daub.py)"
S=$(mktemp -d); ln -s "$RUNS" "$S/s"
(cd "$R" && nix develop --command bash -c "cd $S && python3 -I $OLD/light.py ek-A ek-M ek-X ek-A && \
  for e in k ek; do for c in rect-lamps bay-sunbeam docking-bay; do REF=\$e-A PYTHONPATH=$OLD python3 -P $OLD/daub.py \$e-M:\$c \$e-X:\$c \$e-A:\$c; done; done") 2>&1
rm -rf "$S"

section "3. The motion films (motiontable.py)"
python3 -I "$T/motiontable.py"

section "4. The switches (switchtable.py)"
python3 -I "$T/switchtable.py" M=$RUNS/w-M X=$RUNS/w-X M-cabin-glow-stays=$RUNS/w-cab-M X-cabin-glow-stays=$RUNS/w-cab-X \
  X0-cabin-glow-stays=$RUNS/w-cab-X0 fair-M=$RUNS/fair-M fair-X=$RUNS/fair-X fair-XB=$RUNS/fair-XB fair-XC=$RUNS/fair-XC

section "5. The lag films (the survey's lag results)"
python3 -I - "$RUNS" <<'PY'
import json, sys
from pathlib import Path
print(f"{'film':26s} {'side':4s} {'median lag':>11s} {'largest lag':>12s} {'median error':>13s} {'largest error':>14s}")
for side in 'MX':
    for p in sorted(Path(sys.argv[1], f'l-{side}', 'results', 'lag').glob('*.json')):
        r = json.loads(p.read_text())
        print(f"{p.stem:26s} {side:4s} {r['lagDraws']:11.3f} {r['lagDrawsMax']:12.3f} {r['errorMedian']:13.5f} {r['errorMax']:14.5f}")
PY

section "6. The walk and the climb (the survey's walk and climb results)"
python3 -I - "$RUNS" <<'PY'
import json, sys
from pathlib import Path
for side in ('M', 'X', 'X0'):
    walk = Path(sys.argv[1], f'k-{side}', 'results', 'walk', 'cabin-walk.json')
    if walk.exists():
        r = json.loads(walk.read_text())
        print(f"walk  {side:3s} settle {r['settleDraws']} draws, per region {r['regionDraws']}")
    climb = Path(sys.argv[1], f'k-{side}', 'results', 'climb', 'cabin-climb.json')
    if climb.exists():
        print(f"climb {side:3s} band peak share {json.loads(climb.read_text())['bandPeakShare']:.4f}")
PY

section "7. The evidence census (census.py)"
python3 -I "$T/census.py" $RUNS/n-rect-lamps $RUNS/n-exit-cabin $RUNS/n-docking-bay $RUNS/n-station-cabin

section "8. The switch cabin under the dynamic render scale on the Mac (dynscale.py, the glow-stays switch)"
python3 -I "$T/dynscale.py" $B/dynlog/d16M $B/dynlog/d16 $B/dynlog/d16X2 $B/dynlog/d8 $B/dynlog/dfix 2>&1

section "9. The frame's cost after a lamp switch (costchart.py, the fair switch at the fixed 2/3)"
C=$B/macdyn/cost; args=()
for run in m-1 m-2 x-1 m-s1 x-s1 xb-s1 xc-s1 m-s2 x-s2 xb-s2 xc-s2; do
  f=$C/cost-$run/time-193-393.txt
  [ -s "$f" ] || continue
  side=$(echo "${run%-*}" | tr a-z A-Z); round=${run#*-}
  args+=("$side round $round|$f")
done
python3 -I "$T/costchart.py" "$D/pics/cost-switch-fair-23.png" "${args[@]}"

section "10. The boil of the fair switch at the fixed 2/3 (boilnoise.py; M's frames were deleted, see tc-bece)"
P=$B/macdyn/pics; args=()
for side in x xb xc; do
  [ -f $P/fair-$side-0.6667/f-000459.png ] && args+=("$(echo $side | tr a-z A-Z)|$P/fair-$side-0.6667|$P/fair-$side-0.6667/size-193-393.txt")
done
if [ ${#args[@]} -gt 0 ]; then
  (cd "$R" && nix develop --command python3 -I "$T/boilnoise.py" "${args[@]}") 2>&1
else
  echo "no frames of the fair switch remain"
fi

section "11. The size ramp of the jambs on main c4c1f1f2f (sizeramp.py, kept as ramp-jambs-main-c4c1f1f2f.txt)"
cat "$D/ramp-jambs-main-c4c1f1f2f.txt"

section "12. Frame time, take T17: the deck and the film rect-bay, M (main) against X (build), paired rounds (table.py)"
if [ -d $B/t17/timing ]; then python3 -I "$T/table.py" $B/t17/timing main build; else echo "T17 did not run"; fi
