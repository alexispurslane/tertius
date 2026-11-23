#!/usr/bin/env bash
# Tool code of t0-gumm. queue-sun.sh: the gate's two runs (fairsun.sh) and their measures (fairmeasure.py, events off and on):
# sun-today on xgy (today's cliff) and sun-cap on xby (A, the cap). A run counts as done when its size log holds 600 draws.
# Each run starts at 22 GiB of free disk or more (it waits for that), and the queue stops under 20 GiB.
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd); F=/private/tmp/t0-gbgj/gumm-fair
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
for run in today:xgy cap:xby; do
  IFS=: read code build <<< "$run"; name=sun-$code
  [ -s $F/$name/fair-on.json ] && continue
  [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
  until [ "$(freegb)" -ge 22 ]; do sleep 60; done
  bash "$T/fairsun.sh" $build $name
  [ "$(cat $F/$name/size-*.txt 2>/dev/null | wc -l)" -ge 600 ] || { echo "$(date +%T) $name: incomplete, see $F/$name/run.log"; continue; }
  grep 'PROBE draw' $F/$name/run.log | sort -u
  for ev in off on; do
    (cd "$R" && nix develop --command python3 "$T/fairmeasure.py" "$code|$F/$name|bay|$ev" 2>&1 | grep -v -i 'warning\|sqlite\|flake')
    mv $F/$name/fair.json $F/$name/fair-$ev.json
  done
done
echo "$(date +%T) queue-sun.sh done"
