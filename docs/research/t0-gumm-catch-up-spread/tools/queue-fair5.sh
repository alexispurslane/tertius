#!/usr/bin/env bash
# Tool code of t0-gumm. queue-fair5.sh: the sweep's c3 (1, 3, 0) again, on xkz (scratch-catch-up-short.patch), since on xcz
# it fell back to today's cliff after draw 3 (queue-fair4.sh's run, kept as bay-start-c3-fallback, equals bay-start-today in
# every measure and traces today's 5.79 catch-up rays per pixel). First a check: h16 (0.5, 16, 0) on xkz must give
# bay-start-h16's pictures bit for bit, since the patch changes nothing for a shape whose end is 8 or more.
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd); F=/private/tmp/t0-gbgj/gumm-fair
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
[ "$(freegb)" -lt 22 ] && { echo "under 22 GiB, no run"; exit 1; }
bash "$T/fairrun.sh" xkz bay start bay-check-h16 "TERTIUS_X_CATCHUP=0.5 16 0 0"
for d in 2 8 199; do n=f-$(printf %06d $d).png; cmp -s $F/bay-check-h16/$n $F/bay-start-h16/$n && r=same || r=DIFFERENT; echo "xkz h16 $n against xcz h16: $r"; done
/bin/rm -f $F/bay-check-h16/*.png
name=bay-start-c3
bash "$T/fairrun.sh" xkz bay start $name "TERTIUS_X_CATCHUP=1 3 0 0" "TERTIUS_X_OWNRAY_LOG=$F/$name"
[ "$(cat $F/$name/size-*.txt 2>/dev/null | wc -l)" -ge 200 ] || { echo "$(date +%T) $name: incomplete, see $F/$name/run.log"; exit 1; }
(cd "$R" && nix develop --command python3 "$T/fairmeasure.py" "c3|$F/$name|bay|start" 2>&1 | grep -v -i 'warning\|sqlite\|flake')
keepn="f-000001.png f-000002.png f-000007.png f-000008.png f-000199.png"
(cd $F/$name && for png in *.png; do [[ " $keepn " == *" $png "* ]] || /bin/rm -f "$png"; done)
echo "$(date +%T) queue-fair5.sh done"
