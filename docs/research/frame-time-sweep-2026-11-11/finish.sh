#!/usr/bin/env bash
# After take 2 (outside the GPU lock, in the dev shell): the passes table of each new trace, then what each pass pays in
# each variant, less the take's own baseline. Run: nix develop --command bash /private/tmp/tertius-sweep-20261111/finish.sh
set -u
. /private/tmp/tertius-sweep-20261111/measure-lib.sh
names=()
for d in $S/trace/deck-*/; do
  n=$(basename $d)
  [ -f $d/run/gpu-timings.json ] || continue
  report $n
  [ -f $d/pays.json ] && names+=($n)
done
base=deck-maind-t2; [ -f $S/trace/deck-maind-2/pays.json ] && base=deck-maind-t2+deck-maind-2
[ -f $S/trace/deck-maind-t2/pays.json ] || { echo "no baseline of take 2 yet"; exit 0; }
variants=()
for n in deck-ssr4h-1 deck-ssr9h-1 deck-ownall-1 deck-transfloor-1 deck-steps-r64 deck-steps-r24 deck-steps-d24 deck-maind-nobloom deck-maind-noguide deck-flightonly-1 deck-fnomath-1 deck-fpow7-1 deck-sphclip-1 deck-maind-nostamps deck-maind-3; do
  [ -f $S/trace/$n/pays.json ] && variants+=($n)
done
python3 -I $S/tools/compare.py $base "${variants[@]}" | tee $S/take2-compare.md
for n in deck-maind-t2 "${variants[@]}"; do echo "$n: $(grep -m1 'GPU clock over' $S/trace/$n/run.log | cut -c1-80)"; done | tee -a $S/take2-compare.md
