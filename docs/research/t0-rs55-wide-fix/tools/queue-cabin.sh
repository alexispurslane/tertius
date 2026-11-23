#!/usr/bin/env bash
# Tool code of t0-rs55. queue-cabin.sh: the cabin's takes of the wide fix, today (m0z, main 86ae0fbc8) against the wide fix
# (m1z), with builds.sh's builds:
#   1. the cabin's start and cut, frozen (t0-gumm's fairrun.sh: orbit's person camera after 60 ticks, the switch's lamps off,
#      the fixed exposure of 4.08 stops; the cut's other camera is chase), each at the world's tick 60, 61 and 62
#      (FAIR_TICKS_ADD 0, 1, 2: the spread between runs), measured by fairmeasure.py;
#   2. the fair lamp switch (t0-gumm's macrun.sh switch, switch+1 and switch+2: the lamps off at draw 193 + k and on at
#      393 + k, the constant render scale 2/3), measured by events.py.
# The runs keep no PNG past their measures but the switch's draws 2 and 8 after off and on. A run starts at 20 GiB of free disk
# or more (the line orchestrator's limit); the queue stops under 20.
T=$(cd "$(dirname "$0")" && pwd); G=$(cd "$T/../../t0-gumm-catch-up-spread/tools" && pwd); R=$(cd "$T/../../../.." && pwd)
F=/private/tmp/t0-gbgj/gumm-fair; M=/private/tmp/t0-gbgj/gumm-mac
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
stop() { [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }; }
for event in start cut; do
  for t in 0 1 2; do
    for side in today:m0z wide:m1z; do
      IFS=: read code build <<< "$side"
      name=rs-cabin-$event-$code+$t
      stop
      FAIR_TICKS_ADD=$t bash "$G/fairrun.sh" $build cabin $event $name
      (cd "$R" && nix develop --command python3 "$G/fairmeasure.py" "$event $code +$t|$F/$name|cabin|$event" 2>&1 | grep -v -i 'warning\|sqlite\|flake')
      /bin/rm -f $F/$name/*.png
    done
  done
done
for k in 0 1 2; do
  for side in today:m0z wide:m1z; do
    IFS=: read code build <<< "$side"
    event=switch; [ $k -gt 0 ] && event=switch+$k
    name=rs-switch-$code+$k
    stop
    bash "$G/macrun.sh" $build $event $name
    (cd "$R" && nix develop --command python3 "$G/events.py" "switch $code +$k|$M/$name|$event" 2>&1 | grep -v -i 'warning\|sqlite\|flake')
    keepn=" f-$(printf %06d $((162 + k))).png f-$(printf %06d $((168 + k))).png f-$(printf %06d $((362 + k))).png f-$(printf %06d $((368 + k))).png "
    (cd $M/$name && for png in *.png; do [[ "$keepn" == *" $png "* ]] || /bin/rm -f "$png"; done)
  done
done
echo "$(date +%T) queue-cabin.sh done"
