#!/usr/bin/env bash
# Tool code of t0-gumm. queue-fair4.sh: the sweep's shape c3 (1, 3, 0), full rate for draws 0 to 2 only (the line
# orchestrator's addition of 2026-11-22, 23:25), as queue-fair3.sh runs its shapes: the bay's start, with the cap (xcz), on
# the reset's clock only, with the own-ray log. It keeps the PNGs of draws 1, 2, 7, 8 and 199.
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd); F=/private/tmp/t0-gbgj/gumm-fair
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
name=bay-start-c3
[ -s $F/$name/fair.json ] && exit 0
[ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
until [ "$(freegb)" -ge 22 ]; do sleep 60; done
bash "$T/fairrun.sh" xcz bay start $name "TERTIUS_X_CATCHUP=1 3 0 0" "TERTIUS_X_OWNRAY_LOG=$F/$name"
[ "$(cat $F/$name/size-*.txt 2>/dev/null | wc -l)" -ge 200 ] || { echo "$(date +%T) $name: incomplete, see $F/$name/run.log"; exit 1; }
(cd "$R" && nix develop --command python3 "$T/fairmeasure.py" "c3|$F/$name|bay|start" 2>&1 | grep -v -i 'warning\|sqlite\|flake')
keepn="f-000001.png f-000002.png f-000007.png f-000008.png f-000199.png"
(cd $F/$name && for png in *.png; do [[ " $keepn " == *" $png "* ]] || /bin/rm -f "$png"; done)
echo "$(date +%T) queue-fair4.sh done"
