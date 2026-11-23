#!/usr/bin/env bash
# Tool code of t0-gumm. queue-fair6.sh: the delayed shape d3 (1, 8, -3): full rate in draws 3 to 7 after the reset only, on
# xdz (scratch-catch-up-delay.patch), with the cap and the reset's clock, the bay's start as the sweep runs it, with the
# own-ray log (the line orchestrator's test of a model, 2026-11-22, 23:35). It keeps the PNGs of draws 1, 2, 7, 8 and 199.
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd); F=/private/tmp/t0-gbgj/gumm-fair
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
[ "$(freegb)" -lt 22 ] && { echo "under 22 GiB, no run"; exit 1; }
name=bay-start-d3
bash "$T/fairrun.sh" xdz bay start $name "TERTIUS_X_CATCHUP=1 8 -3 0" "TERTIUS_X_OWNRAY_LOG=$F/$name"
[ "$(cat $F/$name/size-*.txt 2>/dev/null | wc -l)" -ge 200 ] || { echo "$(date +%T) $name: incomplete, see $F/$name/run.log"; exit 1; }
(cd "$R" && nix develop --command python3 "$T/fairmeasure.py" "d3|$F/$name|bay|start" 2>&1 | grep -v -i 'warning\|sqlite\|flake')
keepn="f-000001.png f-000002.png f-000007.png f-000008.png f-000199.png"
(cd $F/$name && for png in *.png; do [[ " $keepn " == *" $png "* ]] || /bin/rm -f "$png"; done)
echo "$(date +%T) queue-fair6.sh done"
