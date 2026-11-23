#!/usr/bin/env bash
# Tool code of t0-5tyj. combine.sh: joins the stills of rect-lamps, bay-sunbeam and docking-bay into one run folder per
# side, so that daub.py and light.py (which read s/<run>/images/still/<case>[-reference].png) see the three cases in one
# run. k-M and k-X take each case from the still group that holds it (X's bay-sunbeam from its retake s7-X); k-A is s-A.
# ek-<side> joins e-lamps-<side> (rect-lamps at exposure 4.08) and e-bay-<side> (the bay at -4.0).
RUNS=${BASE:-/private/tmp/t0-5tyj}/runs
for side in M X; do
  mkdir -p "$RUNS/k-$side/images/still"
  for c in rect-lamps bay-sunbeam docking-bay; do
    src=$(ls -d "$RUNS"/s[1-7]-$side/images/still/$c.png 2>/dev/null | tail -1)
    [ -n "$src" ] || { echo "no $side still of $c"; continue; }
    ln -sfn "$src" "$RUNS/k-$side/images/still/$c.png"
    ln -sfn "${src%.png}-reference.png" "$RUNS/k-$side/images/still/$c-reference.png"
  done
done
ln -sfn s-A "$RUNS/k-A"
for side in M X A; do
  mkdir -p "$RUNS/ek-$side/images/still"
  for f in "$RUNS/e-lamps-$side"/images/still/*.png "$RUNS/e-bay-$side"/images/still/*.png; do
    [ -e "$f" ] && ln -sfn "$f" "$RUNS/ek-$side/images/still/$(basename "$f")"
  done
done
ls "$RUNS"/k-*/images/still "$RUNS"/ek-*/images/still
