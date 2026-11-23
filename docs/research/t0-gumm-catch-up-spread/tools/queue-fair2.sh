#!/usr/bin/env bash
# Tool code of t0-gumm. queue-fair2.sh: the rest of the fair test after queue-fair.sh's bay start and bay cut, then
# t0-qvm1's region log (regionlog.sh). The engine restarts its draw index at every reset (FrameHistory.Begin:
# DrawIndex = reset ? 0 : last + 1), and the noise of a draw follows its draw index. So in a frozen film the draws after
# a cut back are bit-identical to the draws after the start (bay-cut-today's b-n against bay-start-today's f-n, every
# frames.series row), and a cut moved by 1 or 2 draws shows the same pictures. What a later cut changes in a live world is
# the world: here the spread comes from the start with the world run 1 and 2 ticks longer (FAIR_TICKS_ADD, fairrun.sh).
# The runs: the cabin's start for the three codes, the cabin's cut for today's code (to check the identity in the second
# scene), and per scene and code the start at +1 and +2 ticks. A run with a fair.json is skipped; a run counts as done when
# its size log is complete. After its measure, a run keeps the PNGs of draws 1, 2, 7, 8 and 199 after the event.
# Each run starts at 22 GiB of free disk or more (it waits for that), and the queue stops under 20 GiB.
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd); F=/private/tmp/t0-gbgj/gumm-fair
Q=$(cd "$T/../../t0-qvm1-region-off/tools" && pwd)
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
runs="cabin:start:today:0 cabin:start:none:0 cabin:start:C32:0 cabin:cut:today:0"
for scene in bay cabin; do for add in 1 2; do for code in today none C32; do runs="$runs $scene:start:$code:$add"; done; done; done
for run in $runs; do
  IFS=: read scene event code add <<< "$run"
  case $code in today) build=xgz; shape='1 8 0' ;; none) build=xcz; shape='0 32 0' ;; C32) build=xcz; shape='0.03125 32 0' ;; esac
  draws=200; [ $event = cut ] && draws=301
  name=$scene-$event-$code; [ $add = 0 ] || name=$scene-start+t$add-$code
  [ -s $F/$name/fair.json ] && continue
  [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
  until [ "$(freegb)" -ge 22 ]; do sleep 60; done
  FAIR_TICKS_ADD=$add bash "$T/fairrun.sh" $build $scene $event $name "TERTIUS_X_CATCHUP=$shape"
  [ "$(cat $F/$name/size-*.txt 2>/dev/null | wc -l)" -ge $draws ] || { echo "$(date +%T) $name: incomplete, see $F/$name/run.log"; continue; }
  (cd "$R" && nix develop --command python3 "$T/fairmeasure.py" "$code|$F/$name|$scene|$event" 2>&1 | grep -v -i 'warning\|sqlite')
  if [ -s $F/$name/fair.json ]; then
    p=f; [ $event = cut ] && p=b
    keepn="$p-000001.png $p-000002.png $p-000007.png $p-000008.png $p-000199.png"
    (cd $F/$name && for png in *.png; do [[ " $keepn " == *" $png "* ]] || /bin/rm -f "$png"; done)
  fi
done
echo "$(date +%T) queue-fair2.sh done"
bash "$Q/regionlog.sh" >> /private/tmp/t0-gbgj/logs/qvm1-regionlog.log 2>&1
echo "$(date +%T) regionlog.sh done, see /private/tmp/t0-gbgj/logs/qvm1-regionlog.log"
