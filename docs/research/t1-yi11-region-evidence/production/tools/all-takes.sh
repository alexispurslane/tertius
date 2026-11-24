#!/bin/bash
# Runs the takes of each part for prod and base, measures them, and removes the part's take PNGs after a good measure.
set -u
W=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144
T=$(cd "$(dirname "$0")" && pwd)
cd "$W"
run() { echo "== take $* $(date +%T) free $(/bin/df -g / | tail -1 | awk '{print $4}') GiB"; nix develop --command python3 -I $T/takes.py "$@" > /private/tmp/t1-yi11-prod/takes/log-$(echo "$*" | tr ' ' '-').txt 2>&1 || { echo "FAILED $*"; tail -20 /private/tmp/t1-yi11-prod/takes/log-$(echo "$*" | tr ' ' '-').txt; exit 1; }; }
measure() { nix develop --command python3 -I $T/measure.py "$1" > /private/tmp/t1-yi11-prod/out/$1.txt 2>&1 || { echo "MEASURE FAILED $1"; tail -20 /private/tmp/t1-yi11-prod/out/$1.txt; exit 1; }; echo "measured $1"; }
for part in "$@"; do
  case $part in
    walk) for b in prod base; do for r in switch off; do run walk $b $r; done; done ;;
    turned) run turned prod; run turned base ;;
    bayauto) run bay prod auto; run bay base auto ;;
    switch) run switch prod; run switch base; run switchfilm prod; run switchfilm base ;;
    cut) for b in prod base; do run cut $b a; run cut $b b; done ;;
  esac
  measure $part
  find /private/tmp/t1-yi11-prod/takes -name '*.png' -delete
  du -sh /private/tmp/t1-yi11-prod/takes
done
echo ALL DONE
