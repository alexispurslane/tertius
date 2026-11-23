#!/usr/bin/env bash
# Tool code of t0-gumm. macrun.sh <build> <event> <name> [VAR=value...]: one Mac run of an event film at 1920 x 1080 and
# the constant render scale 2/3 (1280 x 720, played back through TERTIUS_X_SIZE_REPLAY), with the binary
# /private/tmp/t0-gbgj/bins/<build>/probe-bin (builds.sh), into /private/tmp/t0-gbgj/gumm-mac/<name>/. The extra
# VAR=value words go into its environment, such as TERTIUS_X_CATCHUP="0.0625 32 0". The events:
#   switch, switch+1, switch+2: the switch film (X5's survey, switch_script of the cabin; film frame f-k is draw 33 + k),
#     the lamps off at draw 193 + k and on at 393 + k;
#   start: start.json, the cabin's person camera filmed from draw 0 for 200 frames (f-k is draw k). A headless run's
#     first frame is a still of 33 draws (FrameHistory.IsStill: its step is endless), so the start runs the build
#     <build>s (builds.sh: with scratch-first-filmed.patch) with TERTIUS_X_FIRST_FILMED=1, which draws it once;
#   cut, cut+1, cut+2: cut.json, the cabin's person camera, film a (a-k is draw 33 + k, 100 + k frames), one frame x on
#     the chase camera (draw 133 + k), then the person camera again, film b from draw 134 + k. Each camera change is a cut,
#     and the second restarts every history in the cabin's view (the history log: the mean length falls from 25.6 to
#     0.98 draws at b-000000). A run of ticks without a draw is no cut, and a still resets but draws 33 draws for one
#     picture, so neither serves.
# A run writes frames.series (the region means of every frame), the size log, the history log and the own-ray log, and
# keeps the PNGs of 20 draws before each event to 61 after it, and of the last 20 frames of start and cut (their settled
# level). It holds the GPU lock in its shared mode. TIMING=1 instead keeps no PNG and holds the lock alone, and writes the
# time log (time-*.txt: "<frame> <GPU span ms> ..." per collected frame) into <name>/.
# REPLAY=<size log> plays back that log's scales (a card log of cardrun.sh, "<draw> <width> <height> <scale>" per draw,
# draw for draw; its last scale holds for 5 more draws) in place of the constant 2/3, and the run checks that its own size
# log gives the same scale on every draw. VIDEO=1 keeps every draw of the video window instead: the switch's film frames
# f 140 to 459 (draws 173 to 492), the start's f 0 to 119 (draws 0 to 119), the cut's a 80 to 99, x and b 0 to 119
# (draws 113 to 253).
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
  start) script=$N/start.json; draws=200; keep="$(f f 0 61)$(f f 180 199)"; build=${build}s; set -- "$@" TERTIUS_X_FIRST_FILMED=1 ;;
  cut) script=$O/cut+$k.json; draws=$((334 + k))
       python3 -I -c "import json, sys; d = json.load(open(sys.argv[1])); d['steps'][3]['frames'] += $k; json.dump(d, open(sys.argv[2], 'w'), indent=1)" $N/cut.json $script
       keep="$(f a $((80 + k)) $((99 + k)))x-000000,$(f b 0 61)$(f b 180 199)" ;;
  *) echo "no event $event"; exit 1 ;;
esac
if [ "${VIDEO:-}" = 1 ]; then
  case ${event%+*} in
    switch) keep="$(f f $((140 + k)) $((459 + k)))" ;;
    start) keep="$(f f 0 119)" ;;
    cut) keep="$(f a $((80 + k)) $((99 + k)))x-000000,$(f b 0 119)" ;;
  esac
fi
rm -rf $out; mkdir -p $out
if [ -n "${REPLAY:-}" ]; then
  head -n $draws "$REPLAY" > $out/replay.txt
  last=$(tail -1 $out/replay.txt | awk '{print $4}')
  for ((d = $(wc -l < $out/replay.txt); d < draws + 5; d++)); do echo "$d 0 0 $last"; done >> $out/replay.txt
else
  for ((d = 0; d < draws + 5; d++)); do echo "$d 0 0 0.6667"; done > $out/replay.txt
fi
lock=(--shared); logs=(TERTIUS_X_HISTORY_LOG=$out TERTIUS_X_OWNRAY_LOG=$out TERTIUS_PROBE_KEEP=${keep%,})
[ "${TIMING:-}" = 1 ] && { lock=(); logs=(TERTIUS_X_TIME_LOG=$out TERTIUS_PROBE_KEEP=); }
echo "$(date +%T) $name: $build $event $*, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB"
cd "$R"
env "${lamps[@]}" TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_REPLAY=$out/replay.txt \
  TERTIUS_X_SIZE_LOG=$out "${logs[@]}" TERTIUS_PROBE_SERIES=$out/frames.series "$@" \
  nix develop --command bash tools/gpu-lock.sh ${lock[@]+"${lock[@]}"} $out/lock.log $B/bins/$build/probe-bin/Tertius.Demo \
  --script $script --headless --out $out --size 1920x1080 > $out/run.log 2>&1
echo "$(date +%T) $name exit $? $(wc -l < $out/size-*.txt 2>/dev/null) draws, $(ls $out/*.png 2>/dev/null | wc -l) pictures"
if [ -n "${REPLAY:-}" ]; then
  echo "$(date +%T) $name replay check: $(awk 'NR == FNR {want[$1] = $4; next} {n++; d = $4 - want[$1]; if (d > 1e-4 || d < -1e-4) bad++}
    END {printf "%d draws, %d with another scale than the log", n, bad}' $out/replay.txt $out/size-*.txt)"
fi
