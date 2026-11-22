#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-variants.sh: the experiment on the cost block after a lamp switch, variants XB
# (../scratch-light-evidence-cap.patch) and XC (../scratch-plan-rate-at-fast-cap.patch) of the line's head, then one cost
# session of all four codes. Steps, one heavy command at a time:
#   1. the builds (playbuild.sh xb, then xc), one after the other, each when the disk has at least 22 GiB free;
#   2. per variant, the survey's fair cabin switch at the fixed 2/3 (take.sh fair-XB, fair-XC), for the settle;
#   3. per variant, the pictures of the video's frames (film "f" 140 to 459) at the constant 2/3, as fair-x-0.6667 was
#      taken (queue-mac7.sh), for the boil and the side-by-side videos, when the disk has at least 22 GiB free;
#   4. the cost session (macdyn.sh cost): two rounds of M, X, XB and XC in turn, each run when the one-minute load is
#      under 8, for at most 120 minutes in all, into cost/cost-<side>-s<round>, so that the four codes have runs at a like
#      load.
# A step whose output exists is skipped, so that a stopped queue resumes.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd)
export BASE=/private/tmp/t0-5tyj; B=$BASE
freegib() { df -BG / | awk 'NR == 2 { gsub("G", "", $4); print $4 }'; }
load() { uptime | sed 's/.*load averages*: *//; s/,.*//; s/\..*//'; }
for side in xb xc; do
  until [ -x $B/${side}play/probe-bin/Tertius.Demo ]; do
    until [ "$(freegib)" -ge 22 ]; do sleep 60; done
    echo "$(date +%T) build $side, $(freegib) GiB free"
    bash "$T/playbuild.sh" $side || sleep 60
  done
done
echo "$(date +%T) builds done, $(freegib) GiB free"
for side in xb xc; do
  PROBE=$B/${side}play/probe-bin bash "$T/take.sh" fair-$(echo $side | tr a-z A-Z) switch cabin
done
for side in xb xc; do
  [ -f $B/macdyn/pics/fair-$side-0.6667/f-000459.png ] && continue
  until [ "$(freegib)" -ge 22 ]; do sleep 60; done
  KEEP=$(for k in $(seq 140 459); do printf 'f-%06d,' $k; done) bash "$T/macdyn.sh" pictures switch $side 0.6667 fair-$side-0.6667
done
deadline=$((SECONDS + 7200))
for round in 1 2; do
  for side in m x xb xc; do
    [ -s $B/macdyn/cost/cost-$side-s$round/time-193-393.txt ] && continue
    until [ "$(load)" -lt 8 ] || [ $SECONDS -ge $deadline ]; do sleep 20; done
    [ $SECONDS -ge $deadline ] && { echo "$(date +%T) gave up: the load stayed at 8 or more"; exit 1; }
    bash "$T/macdyn.sh" cost switch $side cost-$side-s$round
  done
done
echo "$(date +%T) queue-variants done"
