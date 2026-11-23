#!/usr/bin/env bash
# Tool code of t0-rs55. queue-boil.sh: the check of the line orchestrator's reading of the boil of draw 1 (2026-11-23): the
# bay's start as queue-bay.sh runs it, with the engine's debug switch TERTIUS_DIFFUSE_FILTER=off (no diffuse filter), today
# (m0z) and the wide fix (m1z) at the world's tick 420 and 421. If the wide fix's rise of the boil of draw 1 comes from the
# filter (a smoother input gives a lower variance and so less smoothing in draw 1), it leaves with the filter.
# Each run is measured (fairmeasure.py) and keeps the PNGs of draws 0, 1 and 2. A run starts at 20 GiB of free disk or more.
T=$(cd "$(dirname "$0")" && pwd); G=$(cd "$T/../../t0-gumm-catch-up-spread/tools" && pwd); R=$(cd "$T/../../../.." && pwd)
F=/private/tmp/t0-gbgj/gumm-fair
for t in 0 1; do
  for side in today:m0z wide:m1z; do
    IFS=: read code build <<< "$side"
    name=rs-bay-filteroff-$code+$t
    [ "$(df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}')" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
    FAIR_TICKS_ADD=$t bash "$G/fairrun.sh" $build bay start $name TERTIUS_DIFFUSE_FILTER=off
    (cd "$R" && nix develop --command python3 "$G/fairmeasure.py" "filter off $code +$t|$F/$name|bay|start" 2>&1 | grep -v -i 'warning\|sqlite\|flake')
    (cd $F/$name && for png in *.png; do [[ " f-000000.png f-000001.png f-000002.png " == *" $png "* ]] || /bin/rm -f "$png"; done)
  done
done
echo "$(date +%T) queue-boil.sh done"
