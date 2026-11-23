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
if [ -d $B/t17/timing ]; then python3 -I "$T/table.py" $B/t17/timing main build; else echo "T17 did not run on the earlier code: its take on the merged code is section 13h"; fi

PNG=$(mktemp -d)/chart.png  # costchart.py always draws a chart; section 13 prints only its numbers
section "13. The takes on the merged code (README, 'The takes on the merged code'): MF main 3549d3283, XF the build fin of 16d915a67, AF fin with both clamps off"
echo "-- 13a. The stills (stilltable.py)"
SIDES=MF,XF python3 -I "$T/stilltable.py" sF cF
echo "-- 13b. The light share at a fixed exposure and the daub (as section 2, from the runs e-lamps, e-bay, sF and s-AF)"
S=$(mktemp -d); mkdir -p "$S/s"
for side in MF XF AF; do
  mkdir -p "$S/s/eF-$side/images/still"
  ln -s "$RUNS/e-lamps-$side"/images/still/* "$RUNS/e-bay-$side"/images/still/* "$S/s/eF-$side/images/still/"
done
ln -s "$RUNS/sF-MF" "$S/s/kF-MF"; ln -s "$RUNS/sF-XF" "$S/s/kF-XF"; ln -s "$RUNS/s-AF" "$S/s/kF-AF"
(cd "$R" && nix develop --command bash -c "cd $S && python3 -I $OLD/light.py eF-AF eF-MF eF-XF eF-AF && \
  for e in kF eF; do for c in rect-lamps bay-sunbeam docking-bay; do REF=\$e-AF PYTHONPATH=$OLD python3 -P $OLD/daub.py \$e-MF:\$c \$e-XF:\$c \$e-AF:\$c; done; done") 2>&1
rm -rf "$S"
echo "-- 13c. The bay films (motiontable.py)"
SIDES=MF,XF python3 -I "$T/motiontable.py" fF
echo "-- 13d. The switches (switchtable.py)"
python3 -I "$T/switchtable.py" MF=$RUNS/wF-MF XF=$RUNS/wF-XF fair-MF=$RUNS/fair-MF fair-XF=$RUNS/fair-FIN
echo "-- 13e. The boil of XF's fair switch (boilnoise.py at the time of the take; its frames were deleted after it)"
cat "$D/final/boil-fair-fin.txt"
echo "-- 13f. The cabin ceiling's specks: the top third of each cabin still (ceilingspecks.py)"
(cd "$R" && nix develop --command python3 -I "$T/ceilingspecks.py" M=$RUNS/s1-M M=$RUNS/s2-M M=$RUNS/s6-M MF=$RUNS/cF-MF \
  X=$RUNS/s1-X X=$RUNS/s2-X X=$RUNS/s6-X X=$RUNS/s7-X XF=$RUNS/cF-XF) 2>&1
echo "-- 13g. The paired cost runs of the cap before (xb) and after (fin) the merge, and fin's own session (costchart.py)"
C=$B/macdyn/cost; args=()
for run in xb-s1 xb-s2 fin-s1 fin-s2 xb-p1 fin-p1 xb-p2 fin-p2; do
  f=$C/cost-$run/time-193-393.txt
  [ -s "$f" ] && args+=("$run|$f")
done
python3 -I "$T/costchart.py" "$PNG" "${args[@]}" 2>&1 | grep -v "^$PNG"
echo "-- 13h. T17 on the merged code: main 3549d3283 (main) against bcece06cb (build), paired rounds (table.py)"
python3 -I "$T/table.py" $B/t17f/timing main build
echo "-- 13i. The card's render sizes and GPU spans through the fair switch on the merged code, job f8b8cba5 (dynscale.py, costchart.py)"
python3 -I "$T/dynscale.py" "$D/cloud-final-16ms" 2>&1
TITLE="The card, 16.67 ms, the merged code" python3 -I "$T/costchart.py" "$PNG" "XF card|$D/cloud-final-16ms/time-193-393.txt" 2>&1 | grep -v "^$PNG"
rm -rf "$(dirname "$PNG")"
