#!/usr/bin/env bash
# Tool code of t0-gumm. fairrun.sh <build> <bay|cabin> <start|cut[+k]> <name> [VAR=value...]: one Mac run of the fair test
# (the line orchestrator's order of 2026-11-22: a start and a camera cut in scenes lit by bounce light) at 1920 x 1080 and
# the constant render scale 2/3 (played back through TERTIUS_X_SIZE_REPLAY), with the binary
# /private/tmp/t0-gbgj/bins/<build>/probe-bin (builds.sh: xgz or xcz), into /private/tmp/t0-gbgj/gumm-fair/<name>/.
# The films are frozen: the world runs its ticks first, then every frame is a capture right after a capture, which
# TERTIUS_X_FROZEN=1 (scratch-frozen.patch) draws once without a step of time. So the sun, the station's turn and every
# lamp hold still, and only the reset differs between the draws. TERTIUS_X_FIRST_FILMED=1 draws the first frame once.
# The scenes:
#   bay: demo-surface's bay-camera after 420 ticks (the survey's still bay-sunbeam: the bay lit by one sunbeam and its
#        bounce), the fixed exposure of -4.0 stops; the cut's other camera is hold-camera;
#   cabin: orbit's person camera after 60 ticks (the switch film's view), with the switch's lamps (z:-12:4) off in every
#        draw, the fixed exposure of 4.08 stops; the cut's other camera is chase.
# The events: start (film f, 200 frames: f-n is draw n), cut+k (film a of 100 + k frames, a-n is draw n; one frame x on
# the other camera, draw 100 + k; then film b of 200 frames on the first camera, b-n is draw 101 + k + n: the cut back
# restarts every history at b-000000).
# It keeps the PNGs of the start's draws 0 to 61 and 180 to 199, and of the cut's a 80 + k to 99 + k, x, b 0 to 61 and
# b 180 to 199, and writes frames.series (the region means of every frame) and the size log. It holds the GPU lock in its
# shared mode. The extra VAR=value words go into its environment, such as TERTIUS_X_CATCHUP="0 32 0". FAIR_TICKS_ADD=n runs
# the world n ticks more before the frozen film (queue-fair2.sh's spread: what a later event changes in a live world).
set -uo pipefail
build=$1; scene=$2; event=$3; name=$4; shift 4
N=$(cd "$(dirname "$0")/.." && pwd); R=$(cd "$N/../../.." && pwd)
B=/private/tmp/t0-gbgj; O=$B/gumm-fair; out=$O/$name; mkdir -p $O
k=0; [[ $event == *+* ]] && k=${event#*+}
case $scene in
  bay) world=demo-surface; camera=bay-camera; other=hold-camera; ticks=420; light=(TERTIUS_X_NONE=1); exposure=-4.0 ;;
  cabin) world=orbit; camera=person; other=chase; ticks=60; light=(TERTIUS_PROBE_LAMP_SEL=z:-12:4 TERTIUS_PROBE_LAMP_OFF=0-100000); exposure=4.08 ;;
  *) echo "no scene $scene"; exit 1 ;;
esac
ticks=$((ticks + ${FAIR_TICKS_ADD:-0}))
f() { local p=$1 a=$2 b=$3; for ((i = a; i <= b; i++)); do printf '%s-%06d,' $p $i; done; }
case ${event%+*} in
  start) draws=200; keep="$(f f 0 61)$(f f 180 199)"; films="f:200" ;;
  cut) draws=$((301 + k)); keep="$(f a $((80 + k)) $((99 + k)))x-000000,$(f b 0 61)$(f b 180 199)"; films="a:$((100 + k)) x:1 b:200" ;;
  *) echo "no event $event"; exit 1 ;;
esac
rm -rf $out; mkdir -p $out
python3 -I - "$world" "$camera" "$other" "$ticks" "$films" > $out/script.json <<'PY'
import json, sys
world, camera, other, ticks, films = sys.argv[1:6]
steps = [{"do": "camera", "entity": camera}, {"do": "run", "ticks": int(ticks)}]
for film in films.split():
    name, frames = film.split(':')
    if name == 'x':
        steps.append({"do": "camera", "entity": other})
    elif name == 'b':
        steps.append({"do": "camera", "entity": camera})
    steps += [{"do": "capture", "name": f"{name}-{n:06d}"} for n in range(int(frames))]
json.dump({"scene": world, "window": {"width": 1920, "height": 1080}, "steps": steps}, sys.stdout, indent=1)
PY
for ((d = 0; d < draws + 5; d++)); do echo "$d 0 0 0.6667"; done > $out/replay.txt
echo "$(date +%T) $name: $build $scene $event $*, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB"
cd "$R"
env "${light[@]}" TERTIUS_PROBE_EXPOSURE=$exposure TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_REPLAY=$out/replay.txt \
  TERTIUS_X_SIZE_LOG=$out TERTIUS_PROBE_KEEP=${keep%,} TERTIUS_PROBE_SERIES=$out/frames.series TERTIUS_X_FIRST_FILMED=1 \
  TERTIUS_X_FROZEN=1 "$@" \
  nix develop --command bash tools/gpu-lock.sh --shared $out/lock.log $B/bins/$build/probe-bin/Tertius.Demo \
  --script $out/script.json --headless --out $out --size 1920x1080 > $out/run.log 2>&1
echo "$(date +%T) $name exit $? $(wc -l < $out/size-*.txt 2>/dev/null) draws, $(ls $out/*.png 2>/dev/null | wc -l) pictures"
