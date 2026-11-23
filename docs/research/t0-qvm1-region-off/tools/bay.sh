#!/usr/bin/env bash
# Tool code of the region take (t0-qvm1). bay.sh: t0-acxm's measure of the bay film's trail, which is the survey's own:
# the motion film rect-bay (its level and blotch against the stills of the same states) and the lag film bay-sun (median
# lag in draws and lag error), at the fixed render scale 2/3, through t0-gbgj's take.sh, on xfd (b-QD, l-QD: with region
# evidence) and xfn (b-QN, l-QN: without it). A run whose survey wrote a result is kept. Each run starts at 22 GiB of
# free disk or more (it waits for that), and the script stops under 20 GiB.
G=$(cd "$(dirname "$0")/../../t0-gbgj-combined-check/tools" && pwd)
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
for run in b-QD:xfd:motion:rect-bay b-QN:xfn:motion:rect-bay l-QD:xfd:lag:bay-sun l-QN:xfn:lag:bay-sun; do
  IFS=: read name build part case <<< "$run"
  [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
  until [ "$(freegb)" -ge 22 ]; do sleep 60; done
  bash "$G/take.sh" $name $part $case $build TERTIUS_X_RENDER_SCALE=0.6667
done
echo "$(date +%T) bay.sh done"
