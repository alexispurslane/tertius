#!/usr/bin/env bash
# Tool code of t0-gumm. queue-fair3.sh: the line orchestrator's sweep of 2026-11-22, 23:20: the bay's start (fairrun.sh) for
# catch-up shapes between today's cliff and none, with the cap (build xcz) and on the reset's clock only (the shape's fourth
# value 0: a light change makes no history young; at a frozen start nothing else changes). The shapes (peak, end, fall):
#   h16 (0.5, 16, 0) and q32 (0.25, 32, 0): about today's ray total at half and a quarter of its peak;
#   e32 (0.125, 32, 0): half of today's total;
#   f32 (0.5, 32, 1): front-loaded, the share falls from 1/2 at the reset to 0 at draw 32, a total of 0.5 * 16.5 = 8.25 rays
#       per pixel, about today's.
# Each run also writes the own-ray log (TERTIUS_X_OWNRAY_LOG). own-today and own-none run today's code and none again only
# for their own-ray logs (their pictures repeat bay-start-today and bay-start-none, which the queue checks).
# A run with a fair.json (or, for own-*, an own-ray log) is skipped; a run counts as done when its size log is complete.
# After its measure, a run keeps the PNGs of draws 1, 2, 7, 8 and 199. Each run starts at 22 GiB of free disk or more (it
# waits for that), and the queue stops under 20 GiB.
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd); F=/private/tmp/t0-gbgj/gumm-fair
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
for run in h16:xcz:"0.5 16 0 0" q32:xcz:"0.25 32 0 0" e32:xcz:"0.125 32 0 0" f32:xcz:"0.5 32 1 0" own-today:xgz:"1 8 0" own-none:xcz:"0 32 0"; do
  IFS=: read code build shape <<< "$run"
  name=bay-start-$code; [[ $code == own-* ]] && name=bay-$code
  [ -s $F/$name/fair.json ] && continue
  [[ $code == own-* ]] && compgen -G "$F/$name/ownrays-*.txt" > /dev/null && continue
  [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
  until [ "$(freegb)" -ge 22 ]; do sleep 60; done
  bash "$T/fairrun.sh" $build bay start $name "TERTIUS_X_CATCHUP=$shape" "TERTIUS_X_OWNRAY_LOG=$F/$name"
  [ "$(cat $F/$name/size-*.txt 2>/dev/null | wc -l)" -ge 200 ] || { echo "$(date +%T) $name: incomplete, see $F/$name/run.log"; continue; }
  if [[ $code == own-* ]]; then
    ref=$F/bay-start-${code#own-}
    for d in 2 8 199; do cmp -s $F/$name/f-$(printf %06d $d).png $ref/f-$(printf %06d $d).png && r=same || r=DIFFERENT; echo "$name f-$d against $ref: $r"; done
    (cd $F/$name && /bin/rm -f *.png)
    continue
  fi
  (cd "$R" && nix develop --command python3 "$T/fairmeasure.py" "$code|$F/$name|bay|start" 2>&1 | grep -v -i 'warning\|sqlite')
  if [ -s $F/$name/fair.json ]; then
    keepn="f-000001.png f-000002.png f-000007.png f-000008.png f-000199.png"
    (cd $F/$name && for png in *.png; do [[ " $keepn " == *" $png "* ]] || /bin/rm -f "$png"; done)
  fi
done
echo "$(date +%T) queue-fair3.sh done"
