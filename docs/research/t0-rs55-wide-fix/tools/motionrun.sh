#!/usr/bin/env bash
# Tool code of t0-rs55. motionrun.sh <build> <turn|walk> <name> [VAR=value...]: one Mac run of a film of the motion take
# (scripts.py's turn.json or walk.json, the cabin's person camera, the fixed exposure of 4.08 stops) at 1920 x 1080, with the
# binary /private/tmp/t0-gbgj/bins/<build>/probe-bin (builds.sh: m0v, m1v or m2v), into /private/tmp/t0-gbgj/rs-motion/<name>/.
# REPLAY=<size log> plays back that card log's scales ("<draw> <width> <height> <scale>", draw for draw; its last scale holds
# past its end) through TERTIUS_X_SIZE_REPLAY under TERTIUS_X_FRAME_TIME_TARGET=16.6667, and the run checks its own size log
# against it (1e-4). Without REPLAY it plays the constant 2/3. The window of the motion:
#   turn: f 50 to 59, g000 to g059 (the turn) and h 0 to 59: draws 83 to 212;
#   walk: f 170 to 179, g 0 to 119 (the walk) and h 0 to 59: draws 203 to 392.
# A run keeps the PNG of every draw of the window, frames.series and the size log, and with the band builds writes the slow
# diffuse history's length of every draw of the window (TERTIUS_X_HISTORY_DUMP, history-<draw>-<w>x<h>.u8). It holds the
# GPU lock in its shared mode. TIMING=1 instead keeps no PNG and no dump, holds the lock alone, and writes the time log
# (time-*.txt: "<frame> <GPU span ms> ..." per collected frame).
set -uo pipefail
build=$1; film=$2; name=$3; shift 3
T=$(cd "$(dirname "$0")" && pwd); W=$(cd "$T/.." && pwd); R=$(cd "$W/../../.." && pwd)
B=/private/tmp/t0-gbgj; out=$B/rs-motion/$name
f() { local p=$1 a=$2 b=$3; for ((i = a; i <= b; i++)); do printf '%s-%06d,' $p $i; done; }
case $film in
  turn) draws=333; from=83; to=212; keep="$(f f 50 59)$(for k in $(seq 0 59); do printf 'g%03d-000000,' $k; done)$(f h 0 59)" ;;
  walk) draws=533; from=203; to=392; keep="$(f f 170 179)$(f g 0 119)$(f h 0 59)" ;;
  *) echo "no film $film"; exit 1 ;;
esac
rm -rf $out; mkdir -p $out
if [ -n "${REPLAY:-}" ]; then
  head -n $draws "$REPLAY" > $out/replay.txt
  last=$(tail -1 $out/replay.txt | awk '{print $4}')
  for ((d = $(wc -l < $out/replay.txt); d < draws + 5; d++)); do echo "$d 0 0 $last"; done >> $out/replay.txt
else
  for ((d = 0; d < draws + 5; d++)); do echo "$d 0 0 0.6667"; done > $out/replay.txt
fi
lock=(--shared); logs=(TERTIUS_X_HISTORY_DUMP=$out:$from-$to TERTIUS_PROBE_KEEP=${keep%,})
[ "${TIMING:-}" = 1 ] && { lock=(); logs=(TERTIUS_X_TIME_LOG=$out TERTIUS_PROBE_KEEP=); }
echo "$(date +%T) $name: $build $film $*, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB"
cd "$R"
env TERTIUS_X_NONE=1 TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_REPLAY=$out/replay.txt \
  TERTIUS_X_SIZE_LOG=$out "${logs[@]}" TERTIUS_PROBE_SERIES=$out/frames.series "$@" \
  nix develop --command bash tools/gpu-lock.sh ${lock[@]+"${lock[@]}"} $out/lock.log $B/bins/$build/probe-bin/Tertius.Demo \
  --script $W/$film.json --headless --out $out --size 1920x1080 > $out/run.log 2>&1
echo "$(date +%T) $name exit $? $(wc -l < $out/size-*.txt 2>/dev/null) draws, $(ls $out/*.png 2>/dev/null | wc -l) pictures, $(ls $out/*.u8 2>/dev/null | wc -l) dumps"
echo "$(date +%T) $name replay check: $(awk 'NR == FNR {want[$1] = $4; next} {n++; d = $4 - want[$1]; if (d > 1e-4 || d < -1e-4) bad++}
  END {printf "%d draws, %d with another scale than the log", n, bad}' $out/replay.txt $out/size-*.txt)"
