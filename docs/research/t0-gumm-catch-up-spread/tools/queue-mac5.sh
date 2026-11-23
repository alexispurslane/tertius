#!/usr/bin/env bash
# Tool code of t0-gumm. queue-mac5.sh: the picture runs of the final three-pane videos (videos.sh), by the line's video
# rules: each code under its own card scale log, the second run of card job 3 (logs/card-shapes3/<film>-<code>-r2), and
# every draw of the video window kept (macrun.sh with REPLAY and VIDEO=1). The codes: A the cap (xb), today's cliff
# (xg with 1 8 0), C32 the cap with the share (0.03125, 32, 0) (xc). The start has no film of its own on the card, so
# it plays back the switch film's log from draw 0 (the card's start window). Each run goes to
# /private/tmp/t0-gbgj/gumm-mac/v-<event>-<code>, whose PNGs compact.sh leaves alone. A run whose size log is complete is
# kept. Each run starts at 22 GiB of free disk or more (it waits for that), and the queue stops under 20 GiB.
T=$(cd "$(dirname "$0")" && pwd); N=$(cd "$T/.." && pwd); O=/private/tmp/t0-gbgj/gumm-mac
declare -A DRAWS=([switch]=493 [start]=200 [cut]=334)
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
for event in cut start switch; do
  for code in A today C32; do
    case $code in A) build=xb; card=cap; shape=() ;; today) build=xg; card=1,8,0; shape=('TERTIUS_X_CATCHUP=1 8 0') ;;
      C32) build=xc; card=cap+0.03125,32,0; shape=('TERTIUS_X_CATCHUP=0.03125 32 0') ;; esac
    film=$event; [ $event = start ] && film=switch
    name=v-$event-$code
    [ "$(cat $O/$name/size-*.txt 2>/dev/null | wc -l)" -ge ${DRAWS[$event]} ] && continue
    [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
    until [ "$(freegb)" -ge 22 ]; do sleep 60; done
    REPLAY=$(echo $N/logs/card-shapes3/$film-$card-r2/size-*.txt) VIDEO=1 bash "$T/macrun.sh" $build $event $name "${shape[@]}"
  done
done
