#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-cost.sh: the frame's cost after a lamp switch, the fair cabin switch at the fixed 2/3, M and
# X in turn, two rounds (macdyn.sh cost). Before each run it waits, for at most 90 minutes in all, until the one-minute
# load is under 8. The run then takes the GPU lock alone, and its lock log holds the uptime when it got the lock.
T=$(cd "$(dirname "$0")" && pwd)
deadline=$((SECONDS + 5400))
for round in 1 2; do
  for side in m x; do
    until [ "$(uptime | sed 's/.*load averages*: *//; s/,.*//; s/\..*//')" -lt 8 ] || [ $SECONDS -ge $deadline ]; do sleep 20; done
    [ $SECONDS -ge $deadline ] && { echo "$(date +%T) gave up: the load stayed at 8 or more"; exit 1; }
    bash "$T/macdyn.sh" cost switch $side cost-$side-$round
  done
done
echo "$(date +%T) queue-cost done"
