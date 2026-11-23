#!/usr/bin/env bash
# Tool code of t0-gumm. queue-mac3.sh: the run-to-run spread of the picture measures (rule 2 of the line orchestrator's
# table: a faster settle counts unless its boil in the first 60 draws is higher by more than the spread between runs with
# the event moved by one and two draws). Picture runs (macrun.sh) of the switch and the cut with the event moved by one
# and two draws (switch+1, switch+2, cut+1, cut+2), for today (xg, 1 8 0) and B32 (xg, 0.03125 32 0). The start cannot
# move: its event is draw 0. compact.sh measures them. A run whose size log is complete is kept. It stops under 20 GiB.
T=$(cd "$(dirname "$0")" && pwd); O=/private/tmp/t0-gbgj/gumm-mac
declare -A DRAWS=([switch]=493 [cut]=334)
for code in today B32; do
  shape="1 8 0"; [ $code = B32 ] && shape="0.03125 32 0"
  for event in switch+1 switch+2 cut+1 cut+2; do
    base=${event%+*}; k=${event#*+}
    [ "$(cat $O/$event-$code/size-*.txt 2>/dev/null | wc -l)" -ge ${DRAWS[$base]} ] && continue
    [ "$(df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}')" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
    bash "$T/macrun.sh" xg $event $event-$code "TERTIUS_X_CATCHUP=$shape"
  done
done
