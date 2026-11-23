#!/usr/bin/env bash
# Tool code of t0-rs55. queue-timing.sh [rounds]: the GPU span of the draws after a start and a cut on the Mac, today (m0z,
# main 86ae0fbc8) against the wide fix (m1z), in paired rounds: each round runs the start and the cut of both builds, the
# order of the builds alternating by round. Each run is t0-gumm's macrun.sh with TIMING=1 (the GPU lock held alone, no
# pictures, the constant render scale 2/3, the cabin's person camera; time-*.txt "<frame> <GPU span ms> ..."), into
# /private/tmp/t0-gbgj/gumm-mac/rs-t-<event>-<code>-r<round>. macrun.sh's start runs the build <build>s, so the queue links
# m0zs and m1zs to m0z and m1z (both carry scratch-first-filmed.patch). spans03.py reads the logs.
# A run starts at 20 GiB of free disk or more (the line orchestrator's limit); the queue stops under 20.
T=$(cd "$(dirname "$0")" && pwd); G=$(cd "$T/../../t0-gumm-catch-up-spread/tools" && pwd); B=/private/tmp/t0-gbgj
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
for b in m0z m1z; do ln -sfn $B/bins/$b $B/bins/${b}s; done
rounds=${1:-5}
for ((r = 1; r <= rounds; r++)); do
  order="today:m0z wide:m1z"; [ $((r % 2)) = 0 ] && order="wide:m1z today:m0z"
  for event in start cut; do
    for side in $order; do
      IFS=: read code build <<< "$side"
      [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
      TIMING=1 bash "$G/macrun.sh" $build $event rs-t-$event-$code-r$r
    done
  done
done
echo "$(date +%T) queue-timing.sh done"
