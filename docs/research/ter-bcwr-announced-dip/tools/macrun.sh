#!/usr/bin/env bash
# Tool code of ter-bcwr, from t0-gumm's macrun.sh. macrun.sh <build> <start|cut> <name> [VAR=value...]: one Mac run of an
# event film at 1920 x 1080 with the binary /private/tmp/t0-gbgj/bins/<build>/probe-bin (builds.sh: bt, today, or bb, the
# build, each with the first-filmed patch), into /private/tmp/t0-gbgj/bcwr-mac/<name>/. The events, t0-gumm's films:
#   start: start.json, the cabin's person camera filmed from draw 0 for 200 frames (f-k is draw k), with
#     TERTIUS_X_FIRST_FILMED=1, so that the first frame draws once;
#   cut: cut.json, the cabin's person camera, film a (a-k is draw 33 + k, 100 frames), one frame x on the chase camera
#     (draw 133), then the person camera again, film b from draw 134: the cut back restarts every history.
# REPLAY=<size log> plays back that card log's scales ("<draw> <width> <height> <scale>", draw for draw; its last scale
# holds for 5 more draws) through TERTIUS_X_SIZE_REPLAY, and the run checks its own size log against it. Without REPLAY it
# plays the constant 2/3. VIDEO=1 keeps every draw of the video window: the start's f 0 to 119 (draws 0 to 119), the cut's
# a 80 to 99, x and b 0 to 119 (draws 113 to 253). Without it, a run keeps 20 draws before the event to 61 after it and the
# last 20 frames. A run writes frames.series (the region means of every frame) and the size log, at the fixed exposure of
# 4.08 stops, and holds the GPU lock in its shared mode. TIMING=1 instead keeps no PNG, holds the lock alone, and writes
# the time log.
set -uo pipefail
build=$1; event=$2; name=$3; shift 3
N=$(cd "$(dirname "$0")/../../t0-gumm-catch-up-spread" && pwd); R=$(cd "$N/../../.." && pwd)
B=/private/tmp/t0-gbgj; O=$B/bcwr-mac; out=$O/$name; mkdir -p $O
k=0; [[ $event == *+* ]] && k=${event#*+}
f() { local p=$1 a=$2 b=$3; for ((i = a; i <= b; i++)); do printf '%s-%06d,' $p $i; done; }
lamps=(TERTIUS_X_NONE=1)
case ${event%+*} in
  start) script=$N/start.json; draws=200; keep="$(f f 0 61)$(f f 180 199)"; set -- "$@" TERTIUS_X_FIRST_FILMED=1 ;;
  cut) script=$O/cut+$k.json; draws=$((334 + k))
       python3 -I -c "import json, sys; d = json.load(open(sys.argv[1])); d['steps'][3]['frames'] += $k; json.dump(d, open(sys.argv[2], 'w'), indent=1)" $N/cut.json $script
       keep="$(f a $((80 + k)) $((99 + k)))x-000000,$(f b 0 61)$(f b 180 199)" ;;
  *) echo "no event $event"; exit 1 ;;
esac
if [ "${VIDEO:-}" = 1 ]; then
  case ${event%+*} in
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
