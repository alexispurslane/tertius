#!/usr/bin/env bash
# Tool code of t0-xwdz. tailrun.sh <bay|cabin> <start|cut> <name> [VAR=value...]: one Mac run of t0-gumm's fair test
# (fairrun.sh: a start and a camera cut, frozen films) with the probe build /private/tmp/t0-xwdz/xl/probe-bin (build.sh),
# the control trace and the tail log, at 1920 x 1080 and the headless default render scale 2/3 (1280 x 720), into
# /private/tmp/t0-xwdz/runs/<name>/. It keeps no PNG (TERTIUS_PROBE_KEEP=none, which acts only with TERTIUS_PROBE_SERIES set): the log is the result.
# The films are frozen: the world runs its ticks first, then every frame is a capture right after a capture, which
# TERTIUS_X_FROZEN=1 draws once without a step of time, and TERTIUS_X_FIRST_FILMED=1 draws the first frame once. So only
# the reset differs between the draws. The scenes, as in fairrun.sh:
#   bay: demo-surface's bay-camera after 420 ticks, the fixed exposure of -4.0 stops; the cut's other camera is hold-camera;
#   cabin: orbit's person camera after 60 ticks, the switch's lamps (z:-12:4) off in every draw, the fixed exposure of
#          4.08 stops; the cut's other camera is chase.
# The events: start (film f, 120 frames: f-n is draw n), cut (film a of 100 frames, one frame x on the other camera, then
# film b of 120 frames on the first camera; the cut back restarts every history at b-000000, draw 101).
# It holds the GPU lock in its shared mode: it times nothing.
set -uo pipefail
scene=$1; event=$2; name=$3; shift 3
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd)
B=/private/tmp/t0-xwdz; out=$B/runs/$name
case $scene in
  bay) world=demo-surface; camera=bay-camera; other=hold-camera; ticks=420; light=(TERTIUS_X_NONE=1); exposure=-4.0 ;;
  cabin) world=orbit; camera=person; other=chase; ticks=60; light=(TERTIUS_PROBE_LAMP_SEL=z:-12:4 TERTIUS_PROBE_LAMP_OFF=0-100000); exposure=4.08 ;;
  *) echo "no scene $scene"; exit 1 ;;
esac
case $event in
  start) films="f:120" ;;
  cut) films="a:100 x:1 b:120" ;;
  *) echo "no event $event"; exit 1 ;;
esac
command rm -rf "${out:?}"; mkdir -p $out
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
echo "$(date +%T) $name: $scene $event $*, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB"
cd "$R"
env "${light[@]}" TERTIUS_PROBE_EXPOSURE=$exposure TERTIUS_PROBE_KEEP=none TERTIUS_PROBE_SERIES=$out/frames.series TERTIUS_X_FIRST_FILMED=1 TERTIUS_X_FROZEN=1 \
  TERTIUS_REFLECTION_HISTORY_AGREEMENT=control-trace TERTIUS_X_TAIL_LOG=$out "$@" \
  nix develop --command bash tools/gpu-lock.sh --shared $out/lock.log $B/xl/probe-bin/Tertius.Demo \
  --script $out/script.json --headless --out $out --size 1920x1080 > $out/run.log 2>&1
echo "$(date +%T) $name exit $? $(wc -l < $out/tail.txt 2>/dev/null) draws logged, $(command ls $out/*.png 2>/dev/null | wc -l) pictures"
