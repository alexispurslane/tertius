#!/usr/bin/env bash
# Tool code of t0-gumm. macrun.sh <build> <event> <name> [VAR=value...]: one Mac run of an event film at 1920 x 1080 and
# the constant render scale 2/3 (1280 x 720, played back through TERTIUS_X_SIZE_REPLAY), with the binary
# /private/tmp/t0-gbgj/bins/<build>/probe-bin (builds.sh), into /private/tmp/t0-gbgj/gumm-mac/<name>/. The extra
# VAR=value words go into its environment, such as TERTIUS_X_CATCHUP="0.0625 32 0". The events:
#   switch, switch+1, switch+2: the switch film (X5's survey, switch_script of the cabin; film frame f-k is draw 33 + k),
#     the lamps off at draw 193 + k and on at 393 + k;
#   start: start.json, the cabin's person camera filmed from draw 0 for 200 frames (f-k is draw k);
#   cut, cut+1, cut+2: cut.json, the cabin's person camera, film a (a-k is draw 33 + k, 100 + k frames), one frame x on
#     the chase camera (draw 133 + k), then the person camera again, film b from draw 134 + k. Each camera change is a cut,
#     and the second restarts every history in the cabin's view (the history log: the mean length falls from 25.6 to
#     0.98 draws at b-000000). A run of ticks without a draw is no cut, and a still resets but draws 33 draws for one
#     picture, so neither serves.
# A run writes frames.series (the region means of every frame), the size log, the history log and the own-ray log, and
# keeps the PNGs of 20 draws before each event to 61 after it, and of the last 20 frames of start and cut (their settled
# level). It holds the GPU lock in its shared mode. TIMING=1 instead keeps no PNG and holds the lock alone, and writes the
# time log (time-*.txt: "<frame> <GPU span ms> ..." per collected frame) into <name>/.
set -uo pipefail
build=$1; event=$2; name=$3; shift 3
N=$(cd "$(dirname "$0")/.." && pwd); R=$(cd "$N/../../.." && pwd)
B=/private/tmp/t0-gbgj; O=$B/gumm-mac; out=$O/$name; mkdir -p $O
k=0; [[ $event == *+* ]] && k=${event#*+}
f() { local p=$1 a=$2 b=$3; for ((i = a; i <= b; i++)); do printf '%s-%06d,' $p $i; done; }
lamps=(TERTIUS_X_NONE=1)
case ${event%+*} in
  switch) script=$B/macdyn5/switch.json; draws=$((493 + k))
          lamps=(TERTIUS_PROBE_LAMP_SEL=z:-12:4 TERTIUS_PROBE_LAMP_OFF=$((193 + k))-$((393 + k)))
          keep="$(f f $((140 + k)) $((220 + k)))$(f f $((340 + k)) $((420 + k)))" ;;
  start) script=$N/start.json; draws=200; keep="$(f f 0 61)$(f f 180 199)" ;;
  cut) script=$O/cut+$k.json; draws=$((334 + k))
       python3 -I -c "import json, sys; d = json.load(open(sys.argv[1])); d['steps'][3]['frames'] += $k; json.dump(d, open(sys.argv[2], 'w'), indent=1)" $N/cut.json $script
       keep="$(f a $((80 + k)) $((99 + k)))x-000000,$(f b 0 61)$(f b 180 199)" ;;
  *) echo "no event $event"; exit 1 ;;
esac
rm -rf $out; mkdir -p $out
for ((d = 0; d < draws + 5; d++)); do echo "$d 0 0 0.6667"; done > $out/replay.txt
lock=(--shared); logs=(TERTIUS_X_HISTORY_LOG=$out TERTIUS_X_OWNRAY_LOG=$out TERTIUS_PROBE_KEEP=${keep%,})
[ "${TIMING:-}" = 1 ] && { lock=(); logs=(TERTIUS_X_TIME_LOG=$out TERTIUS_PROBE_KEEP=); }
echo "$(date +%T) $name: $build $event $*, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB"
cd "$R"
env "${lamps[@]}" TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_REPLAY=$out/replay.txt \
  TERTIUS_X_SIZE_LOG=$out "${logs[@]}" TERTIUS_PROBE_SERIES=$out/frames.series "$@" \
  nix develop --command bash tools/gpu-lock.sh ${lock[@]+"${lock[@]}"} $out/lock.log $B/bins/$build/probe-bin/Tertius.Demo \
  --script $script --headless --out $out --size 1920x1080 > $out/run.log 2>&1
echo "$(date +%T) $name exit $? $(wc -l < $out/size-*.txt 2>/dev/null) draws, $(ls $out/*.png 2>/dev/null | wc -l) pictures"
