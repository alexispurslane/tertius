#!/usr/bin/env bash
# Tool code of t0-rs55. queue-timing.sh [rounds]: the GPU span per draw on the Mac, today (m0v, main 86ae0fbc8) against the
# wide fix (m1v) and the cost probe (m2v: each tap's surface test stands for its block mates'), in paired rounds: each round
# runs every event of every code, the order of the codes rotating by one per round. The events:
#   start and cut: t0-gumm's macrun.sh with TIMING=1 (the cabin's person camera; the GPU lock held alone, no pictures, the
#     constant render scale 2/3; time-*.txt "<frame> <GPU span ms> ..."), into
#     /private/tmp/t0-gbgj/gumm-mac/rs-t-<event>-<code>-r<round>. macrun.sh's start runs the build <build>s, so the queue
#     links m0vs, m1vs and m2vs to m0v, m1v and m2v (all carry scratch-first-filmed.patch);
#   turn and walk: motionrun.sh with TIMING=1 at the constant render scale 2/3, into
#     /private/tmp/t0-gbgj/rs-motion/rs-t-<film>-<code>-r<round>.
# Every code draws the same pixels at the constant scale, so the spans compare draw for draw (the card's runs give the spans
# under each side's own dynamic scale). spans03.py and spansmotion.py read the logs. A run starts at 20 GiB of free disk or
# more (the line orchestrator's limit); the queue stops under 20.
# EVENTS="<event>..." runs only those events, in that order, in every round.
T=$(cd "$(dirname "$0")" && pwd); G=$(cd "$T/../../t0-gumm-catch-up-spread/tools" && pwd); B=/private/tmp/t0-gbgj
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
for b in m0v m1v m2v; do ln -sfn $B/bins/$b $B/bins/${b}s; done
codes=(today:m0v wide:m1v probe:m2v)
rounds=${1:-5}
for ((r = 1; r <= rounds; r++)); do
  order=("${codes[@]:$(((r - 1) % 3))}" "${codes[@]:0:$(((r - 1) % 3))}")
  for event in ${EVENTS:-start cut turn walk}; do
    for side in "${order[@]}"; do
      IFS=: read code build <<< "$side"
      [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
      case $event in
        start|cut) TIMING=1 bash "$G/macrun.sh" $build $event rs-t-$event-$code-r$r ;;
        *) TIMING=1 bash "$T/motionrun.sh" $build $event rs-t-$event-$code-r$r ;;
      esac
    done
  done
done
echo "$(date +%T) queue-timing.sh done"
