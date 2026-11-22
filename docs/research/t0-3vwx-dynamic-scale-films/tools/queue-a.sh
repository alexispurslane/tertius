#!/usr/bin/env bash
# Tool code of t0-3vwx. queue-a.sh [name...]: the Mac's picture runs of takes A and B, one after another, through
# macdyn.sh pictures (the shared GPU lock, the default ACES curve and bloom, 4.08 stops, 1920 x 1080), each draw's render
# scale played back from a card's size log (../sizes/<scene>-<m|x>-16.6667/). Each run also writes the history log
# (TERTIUS_X_HISTORY_LOG, scratch-history-log.patch), and on the X builds the region log. It keeps the video's frames:
# the switch film's frames 140 to 459, or the walk's last 60 draws and the 200 after the stop (g 60 to 119, h 0 to 199).
# After each run it checks that the played-back sizes equal the card's log on every draw. A run stops the queue when the
# disk is under 22 GiB before it starts.
#   s-M5, s-X5, w-M5, w-X5: take A, each code on its own card log (M5 on main's log, X5 on Design X's);
#   s-X3, w-X3: take B, X3 on X5's card log; s-M3: the control, M3 on M5's card log (switch only);
#   l-X5: X5's switch log on X5 with the lamps on through the whole run (TERTIUS_PROBE_LAMP_OFF empty): the sizes change
#   97 times under a still light and a still camera, which separates the edges' residual of a size change from that of
#   the light's change. Its own size and history logs are then size-none.txt and history-none.txt.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); S=$(cd "$T/../sizes" && pwd); O=/private/tmp/t0-gbgj/macdyn5
free() { df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}'; }
sw=$(for k in $(seq 140 459); do printf 'f-%06d,' $k; done)
wk=$( (for k in $(seq 60 119); do printf 'g-%06d,' $k; done; for k in $(seq 0 199); do printf 'h-%06d,' $k; done) )
for name in ${@:-s-M5 s-X5 w-M5 w-X5 s-X3 w-X3 s-M3}; do
  case $name in
    s-M5) scene=switch build=m5h log=m ;; s-X5) scene=switch build=x5h log=x ;;
    w-M5) scene=walk build=m5h log=m ;;   w-X5) scene=walk build=x5h log=x ;;
    s-X3) scene=switch build=x3h log=x ;; w-X3) scene=walk build=x3h log=x ;;
    s-M3) scene=switch build=m3h log=m ;;
    l-X5) scene=switch build=x5h log=x ;;
    *) echo "no run $name"; exit 1 ;;
  esac
  if [ $scene = switch ]; then keep=$sw; file=size-193-393.txt; else keep=$wk; file=size-none.txt; fi
  extra=(); own=$file; [ $name = l-X5 ] && extra=(TERTIUS_PROBE_LAMP_OFF=) && own=size-none.txt
  if awk -v a="$(free)" 'BEGIN { exit !(a < 22) }'; then echo "$(date +%T) only $(free) GiB free: stop before $name"; exit 1; fi
  echo "$(date +%T) start $name: $scene on $build, the card's $log log (disk $(free) GiB, load $(sysctl -n vm.loadavg))"
  KEEP=$keep bash "$T/macdyn.sh" pictures $scene $build $S/$scene-$log-16.6667/$file $name TERTIUS_X_HISTORY_LOG=$O/pics/$name ${extra[@]+"${extra[@]}"}
  if cmp -s <(cut -d' ' -f1-3 $S/$scene-$log-16.6667/$file) <(cut -d' ' -f1-3 $O/pics/$name/$own); then same="equal the card's log on every draw"
  else same="DIFFER from the card's log"; fi
  echo "$(date +%T) end $name: $(ls $O/pics/$name/*.png 2>/dev/null | wc -l | tr -d ' ') pictures, played-back sizes $same, disk $(free) GiB"
done
echo "$(date +%T) queue-a done"
