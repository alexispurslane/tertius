#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-pair.sh: paired cost runs of the fair cabin switch at the fixed 2/3 (macdyn.sh cost), the
# build xb (Design X 57b4045ae with ../scratch-light-evidence-cap.patch: the cap before main's step 2) and the build fin
# (the cap on the merged code, queue-fin.sh) in turn, two rounds, each run when the one-minute load is under 8, for at
# most 120 minutes in all, into cost/cost-<side>-p<round>. The pairs separate the cap from main's step 2 and from the
# session in the settled GPU span.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd)
export BASE=/private/tmp/t0-5tyj; B=$BASE
load() { uptime | sed 's/.*load averages*: *//; s/,.*//; s/\..*//'; }
deadline=$((SECONDS + 7200))
for round in 1 2; do
  for side in xb fin; do
    [ -s $B/macdyn/cost/cost-$side-p$round/time-193-393.txt ] && continue
    until [ "$(load)" -lt 8 ] || [ $SECONDS -ge $deadline ]; do sleep 20; done
    [ $SECONDS -ge $deadline ] && { echo "$(date +%T) gave up: the load stayed at 8 or more"; exit 1; }
    bash "$T/macdyn.sh" cost switch $side cost-$side-p$round
  done
done
echo "$(date +%T) queue-pair done"
