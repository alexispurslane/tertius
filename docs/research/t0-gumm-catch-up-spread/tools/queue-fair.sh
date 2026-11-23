#!/usr/bin/env bash
# Tool code of t0-gumm. queue-fair.sh: the fair test's Mac runs (fairrun.sh) and their measures (fairmeasure.py). Per
# scene (bay, cabin) and event (start, cut, cut+1, cut+2), three codes: today (xgz with the shape 1 8 0), none (xcz, the
# cap with 0 32 0: no catch-up ray) and C32 (xcz, the cap with 0.03125 32 0). The cut moved by 1 and 2 draws gives the
# spread between runs. A run counts as done when its size log is complete; the queue checks that, since fairrun.sh
# always exits 0. After its measure, a run keeps only the PNGs f-2, f-8 and f-199 (start) or b-1, b-7 and b-199 (cut: draws 1, 7 and
# 199 after the event). It was stopped after the bay's cut: the cut equals the start, so queue-fair2.sh did the rest.
# Each run starts at 22 GiB of free disk or more (it waits for that), and the queue stops under 20 GiB.
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd); F=/private/tmp/t0-gbgj/gumm-fair
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
for scene in bay cabin; do for event in start cut cut+1 cut+2; do for code in today none C32; do
  case $code in today) build=xgz; shape='1 8 0' ;; none) build=xcz; shape='0 32 0' ;; C32) build=xcz; shape='0.03125 32 0' ;; esac
  k=0; [[ $event == *+* ]] && k=${event#*+}
  draws=200; [[ $event == cut* ]] && draws=$((301 + k))
  name=$scene-$event-$code
  [ -s $F/$name/fair.json ] && continue
  [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
  until [ "$(freegb)" -ge 22 ]; do sleep 60; done
  bash "$T/fairrun.sh" $build $scene $event $name "TERTIUS_X_CATCHUP=$shape"
  [ "$(cat $F/$name/size-*.txt 2>/dev/null | wc -l)" -ge $draws ] || { echo "$(date +%T) $name: incomplete, see $F/$name/run.log"; continue; }
  (cd "$R" && nix develop --command python3 "$T/fairmeasure.py" "$code|$F/$name|$scene|$event" 2>&1 | grep -v -i 'warning\|sqlite')
  if [ -s $F/$name/fair.json ]; then
    if [ $event = start ]; then keepn="f-000002.png f-000008.png f-000199.png"; else keepn="b-000001.png b-000007.png b-000199.png"; fi
    (cd $F/$name && for p in *.png; do [[ " $keepn " == *" $p "* ]] || /bin/rm -f "$p"; done)
  fi
done; done; done
echo "$(date +%T) queue-fair.sh done"
