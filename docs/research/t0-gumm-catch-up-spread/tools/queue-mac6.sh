#!/usr/bin/env bash
# Tool code of t0-gumm. queue-mac6.sh: the Mac's picture runs of the start and the cut with no catch-up ray, B0, the
# shape (0, 32, 0) on xg, at the constant scale 2/3 (macrun.sh), so that the table can say what one catch-up ray per
# restarted pixel (B32 and C32: 1/32 for 32 draws) buys against none. After a light change C32 equals A, which traces
# none, so the switch needs no B0 run. compact.sh measures the runs. A run whose size log is complete is kept. Each run
# starts at 22 GiB of free disk or more (it waits for that), and the queue stops under 20 GiB.
T=$(cd "$(dirname "$0")" && pwd); O=/private/tmp/t0-gbgj/gumm-mac
declare -A DRAWS=([start]=200 [cut]=334)
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
for event in start cut; do
  [ "$(cat $O/$event-B0/size-*.txt 2>/dev/null | wc -l)" -ge ${DRAWS[$event]} ] && continue
  [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
  until [ "$(freegb)" -ge 22 ]; do sleep 60; done
  bash "$T/macrun.sh" xg $event $event-B0 'TERTIUS_X_CATCHUP=0 32 0'
done
