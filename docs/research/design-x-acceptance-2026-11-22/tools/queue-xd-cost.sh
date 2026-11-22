#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-xd-cost.sh: the cost session of experiment XD (macdyn.sh cost, the fair cabin switch at the
# fixed 2/3): XD twice, with X and XB once each as anchors in the same session, in the order xd, x, xd, xb. Each run
# waits until the one-minute load is under 8, for at most 120 minutes in all. A run whose time log exists is skipped.
T=$(cd "$(dirname "$0")" && pwd)
export BASE=/private/tmp/t0-5tyj
deadline=$((SECONDS + 7200))
load() { uptime | sed 's/.*load averages*: *//; s/,.*//; s/\..*//'; }
for run in xd-s1 x-s3 xd-s2 xb-s3; do
  [ -s $BASE/macdyn/cost/cost-$run/time-193-393.txt ] && continue
  until [ "$(load)" -lt 8 ] || [ $SECONDS -ge $deadline ]; do sleep 20; done
  [ $SECONDS -ge $deadline ] && { echo "$(date +%T) gave up: the load stayed at 8 or more"; exit 1; }
  bash "$T/macdyn.sh" cost switch ${run%-*} cost-$run
done
echo "$(date +%T) queue-xd-cost done"
