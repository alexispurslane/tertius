#!/usr/bin/env bash
# Tool code of t0-3vwx. queue-b.sh: take B's runs, one after another. Before each run it waits until the disk has at
# least 22 GiB free (the line orchestrator's limit for a take), checking every 60 s, and it stops the queue if the disk
# falls under 20 GiB during the wait for more than 30 minutes.
#   1. The picture runs of queue-a.sh: s-X3 (X3 on X5's switch log), l-X5 (X5's switch log with the lamps on all run) and
#      s-M3 (M3 on M5's switch log). The walk on X3 is left out: its scale stands still in the film's window.
#   2. The survey's own takes of the switch "cabin" and the walk "cabin-walk" (t0-gbgj's take.sh: renderer-survey.py run
#      with the build's probe, the GPU lock's shared mode per render), each draw's render scale played back from a card
#      log (TERTIUS_X_SIZE_REPLAY, keyed by frame number, so the survey's control runs take the same per-draw sizes), with
#      the sizes logged for the check. They give the settle draws after off, after on and after the walk's stop:
#      sw-X3, sw-X5 (X5's switch log), sw-M5, sw-M3 (M5's switch log), wk-X5 (X5's walk log).
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); S=$(cd "$T/../sizes" && pwd); G=$(cd "$T/../../t0-gbgj-combined-check/tools" && pwd)
free() { df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}'; }
wait_disk() {
  local since=$SECONDS
  until awk -v a="$(free)" 'BEGIN { exit !(a >= 22) }'; do
    if (( SECONDS - since > 1800 )); then echo "$(date +%T) the disk stayed under 22 GiB for 30 minutes ($(free) GiB): stop"; exit 1; fi
    sleep 60
  done
}
for name in ${RUNS:-s-X3 l-X5 s-M3}; do wait_disk; bash "$T/queue-a.sh" $name || exit 1; done
for spec in ${SURVEYS:-sw-X3:x3h:switch:x sw-X5:x5h:switch:x sw-M5:m5h:switch:m sw-M3:m3h:switch:m wk-X5:x5h:walk:x}; do
  IFS=: read -r name build scene log <<< "$spec"
  if [ $scene = switch ]; then part=switch case=cabin file=size-193-393.txt; else part=walk case=cabin-walk file=size-none.txt; fi
  wait_disk
  echo "$(date +%T) survey $name: $part $case on $build, the card's $log log (disk $(free) GiB)"
  bash "$G/take.sh" $name $part $case $build TERTIUS_X_FRAME_TIME_TARGET=16.6667 \
    TERTIUS_X_SIZE_REPLAY=$S/$scene-$log-16.6667/$file TERTIUS_X_SIZE_LOG=/private/tmp/t0-gbgj/runs/$name/sizes
  echo "$(date +%T) end survey $name, disk $(free) GiB"
done
echo "$(date +%T) queue-b done"
