#!/usr/bin/env bash
# Tool code of the region take (t0-qvm1). stills.sh: the survey's still part of exit-cabin and cabin-light-cabin at the
# fixed render scale 2/3 (TERTIUS_X_RENDER_SCALE=0.6667), through t0-gbgj's take.sh (draw 132 and the long reference of
# each still), on three codes: s-QD (xfd, with region evidence), s-QN (xfn, without it) and s-QA (xfd with both history
# clamps off, TERTIUS_A_NO_CLAMP=1: the truth that daub.py measures against). A run whose survey wrote a result is kept.
# Each run starts at 22 GiB of free disk or more (it waits for that), and the script stops under 20 GiB.
G=$(cd "$(dirname "$0")/../../t0-gbgj-combined-check/tools" && pwd)
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
for run in s-QD:xfd s-QN:xfn s-QA:xfd; do
  name=${run%%:*}; build=${run##*:}; extra=(); [ $name = s-QA ] && extra=(TERTIUS_A_NO_CLAMP=1)
  [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
  until [ "$(freegb)" -ge 22 ]; do sleep 60; done
  bash "$G/take.sh" $name still exit-cabin,cabin-light-cabin $build TERTIUS_X_RENDER_SCALE=0.6667 "${extra[@]}"
done
echo "$(date +%T) stills.sh done"
