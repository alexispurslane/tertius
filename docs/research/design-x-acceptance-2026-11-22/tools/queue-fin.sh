#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-fin.sh: the acceptance take of the cap (t0-zmjx) on the merged code, the build "fin"
# (playbuild.sh x with PATCHSET=step2 and OUT=fin: the acceptance branch with the cap, the fix of tc-8142 and main's step
# 2 of the wobble fix). The same three steps as queue-variants.sh, one heavy command at a time:
#   1. the survey's fair cabin switch at the fixed 2/3 (take.sh fair-FIN), for the settle;
#   2. the pictures of the video's frames (film "f" 140 to 459) at the constant 2/3, for the boil, when the disk has at
#      least 22 GiB free;
#   3. two cost runs (macdyn.sh cost), each when the one-minute load is under 8, for at most 120 minutes in all, into
#      cost/cost-fin-s<round>.
# A step whose output exists is skipped, so that a stopped queue resumes.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd)
export BASE=/private/tmp/t0-5tyj; B=$BASE
freegib() { df -BG / | awk 'NR == 2 { gsub("G", "", $4); print $4 }'; }
load() { uptime | sed 's/.*load averages*: *//; s/,.*//; s/\..*//'; }
[ -x $B/finplay/probe-bin/Tertius.Demo ] || { echo "no build fin"; exit 1; }
echo "$(date +%T) take fair-FIN, load $(load)"
PROBE=$B/finplay/probe-bin bash "$T/take.sh" fair-FIN switch cabin
if [ ! -f $B/macdyn/pics/fair-fin-0.6667/f-000459.png ]; then
  until [ "$(freegib)" -ge 22 ]; do sleep 60; done
  echo "$(date +%T) pictures, load $(load), $(freegib) GiB free"
  KEEP=$(for k in $(seq 140 459); do printf 'f-%06d,' $k; done) bash "$T/macdyn.sh" pictures switch fin 0.6667 fair-fin-0.6667
fi
deadline=$((SECONDS + 7200))
for round in 1 2; do
  [ -s $B/macdyn/cost/cost-fin-s$round/time-193-393.txt ] && continue
  until [ "$(load)" -lt 8 ] || [ $SECONDS -ge $deadline ]; do sleep 20; done
  [ $SECONDS -ge $deadline ] && { echo "$(date +%T) gave up: the load stayed at 8 or more"; exit 1; }
  bash "$T/macdyn.sh" cost switch fin cost-fin-s$round
done
echo "$(date +%T) queue-fin done"
