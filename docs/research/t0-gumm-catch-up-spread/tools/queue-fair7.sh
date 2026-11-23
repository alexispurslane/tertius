#!/usr/bin/env bash
# Tool code of t0-gumm. queue-fair7.sh: the bit-for-bit checks of the new scratch patches and the 36-tap fix's runs (the line
# orchestrator's order of 2026-11-22, 23:40), the bay's start as the sweep runs it, with the own-ray log. A check compares
# every row of frames.series (the region means of every draw, to the last digit) with the run that it must equal, and the
# PNGs where both runs kept them:
#   check-delay:   xdz with h16 (0.5, 16, 0) against bay-start-h16 on xcz (scratch-catch-up-delay.patch changes nothing for a
#                  fall of 0 or more);
#   check-wide:    xwz without TERTIUS_X_WIDE_FIX, today's shape (1, 8, 0, 0), against bay-start-today on xgz;
#   check-wide-d3: xwz without TERTIUS_X_WIDE_FIX, d3 (1, 8, -3, 0), against bay-start-d3 on xdz.
# The runs: wide-today (xwz, TERTIUS_X_WIDE_FIX=1, today's shape) and wide-d3 (xwz, TERTIUS_X_WIDE_FIX=1, d3). Every run is
# measured (fairmeasure.py) and keeps the PNGs of draws 1, 2, 4, 7 and 8. A run starts at 20 GiB of free disk or more (the
# line orchestrator's answer 92); the queue stops under 20.
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd); F=/private/tmp/t0-gbgj/gumm-fair
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
same() {  # same <run> <reference run>: every frames.series row, then the PNGs that both kept
  local a=$F/$1 b=$F/$2
  if cmp -s <(sort $a/frames.series) <(sort $b/frames.series); then echo "$1 against $2: frames.series same in all $(wc -l < $a/frames.series) rows"
  else echo "$1 against $2: frames.series DIFFERENT in $(diff <(sort $a/frames.series) <(sort $b/frames.series) | grep -c '^<') rows"; fi
  for p in $b/f-*.png; do [ -e "$p" ] && [ -e $a/$(basename $p) ] && { cmp -s $p $a/$(basename $p) && echo "  $(basename $p) same" || echo "  $(basename $p) DIFFERENT"; }; done
}
for run in check-delay:xdz:"0.5 16 0 0":bay-start-h16: check-wide:xwz:"1 8 0 0":bay-start-today: \
           check-wide-d3:xwz:"1 8 -3 0":bay-start-d3: wide-today:xwz:"1 8 0 0"::1 wide-d3:xwz:"1 8 -3 0"::1; do
  IFS=: read name build shape ref wide <<< "$run"
  [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
  env ${wide:+TERTIUS_X_WIDE_FIX=1} bash "$T/fairrun.sh" $build bay start bay-$name "TERTIUS_X_CATCHUP=$shape" "TERTIUS_X_OWNRAY_LOG=$F/bay-$name"
  [ "$(cat $F/bay-$name/size-*.txt 2>/dev/null | wc -l)" -ge 200 ] || { echo "$(date +%T) bay-$name: incomplete"; exit 1; }
  [ -n "$ref" ] && same bay-$name $ref
  (cd "$R" && nix develop --command python3 "$T/fairmeasure.py" "$name|$F/bay-$name|bay|start" 2>&1 | grep -v -i 'warning\|sqlite\|flake')
  keepn="f-000001.png f-000002.png f-000004.png f-000007.png f-000008.png"
  (cd $F/bay-$name && for png in *.png; do [[ " $keepn " == *" $png "* ]] || /bin/rm -f "$png"; done)
done
echo "$(date +%T) queue-fair7.sh done"
