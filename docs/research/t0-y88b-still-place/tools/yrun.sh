#!/usr/bin/env bash
# Tool code of t0-y88b. yrun.sh <build> <bay|cabin> <name> <draws> <keep from> [VAR=value...]: one Mac run of t0-gumm's
# fair test scene as a frozen film of <draws> draws from a start, with the probe build /private/tmp/t0-y88b/<build>/probe-bin
# (builds.sh), the control trace and t0-xwdz's tail log, at 1920 x 1080 and the headless render scale 2/3 (1280 x 720),
# into /private/tmp/t0-y88b/runs/<name>/. It keeps the PNGs of the draws from <keep from> on (f-n is draw n); a <keep from>
# of <draws> or more keeps none. The extra VAR=value words go into its environment, such as TERTIUS_X_YAW_TEXELS=0.005
# for a slow pan (scratch-yaw-drift.patch). LOG=0 turns off the control trace and the tail log, for stills and films.
# The films are frozen: the world runs its ticks first, then every frame is a capture right after a capture, which
# TERTIUS_X_FROZEN=1 draws once without a step of time, and TERTIUS_X_FIRST_FILMED=1 draws the first frame once. The
# scenes, as in t0-gumm's fairrun.sh:
#   bay: demo-surface's bay-camera after 420 ticks, the fixed exposure of -4.0 stops;
#   cabin: orbit's person camera after 60 ticks, the switch's lamps (z:-12:4) off in every draw, the fixed exposure of
#          4.08 stops.
# It holds the GPU lock in its shared mode: it times nothing. It refuses to start under 20 GiB of free disk.
set -uo pipefail
build=$1; scene=$2; name=$3; draws=$4; from=$5; shift 5
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd)
B=/private/tmp/t0-y88b; out=$B/runs/$name
case $scene in
  bay) world=demo-surface; camera=bay-camera; ticks=420; light=(TERTIUS_X_NONE=1); exposure=-4.0 ;;
  cabin) world=orbit; camera=person; ticks=60; light=(TERTIUS_PROBE_LAMP_SEL=z:-12:4 TERTIUS_PROBE_LAMP_OFF=0-100000); exposure=4.08 ;;
  *) echo "no scene $scene"; exit 1 ;;
esac
free=$(df -k / | awk 'NR == 2 {printf "%d", $4 / 1048576}')
if [ "$free" -lt 20 ]; then echo "$(date +%T) $name: only $free GiB free, under the line's stop of 20: no run"; exit 1; fi
command rm -rf "${out:?}"; mkdir -p $out
python3 -I - "$world" "$camera" "$ticks" "$draws" > $out/script.json <<'PY'
import json, sys
world, camera, ticks, draws = sys.argv[1:5]
steps = [{"do": "camera", "entity": camera}, {"do": "run", "ticks": int(ticks)}]
steps += [{"do": "capture", "name": f"f-{n:06d}"} for n in range(int(draws))]
json.dump({"scene": world, "window": {"width": 1920, "height": 1080}, "steps": steps}, sys.stdout, indent=1)
PY
keep=$(python3 -I -c "import sys; a, b = int(sys.argv[1]), int(sys.argv[2]); print(','.join(f'f-{n:06d}' for n in range(a, b)) or 'none')" "$from" "$draws")
echo "$(date +%T) $name: $build $scene $draws draws, keep from $from, $*, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB"
log=(TERTIUS_REFLECTION_HISTORY_AGREEMENT=control-trace TERTIUS_X_TAIL_LOG=$out)
[ "${LOG:-1}" = 0 ] && log=(TERTIUS_X_NONE=1)
cd "$R"
env "${light[@]}" TERTIUS_PROBE_EXPOSURE=$exposure TERTIUS_PROBE_KEEP=$keep TERTIUS_PROBE_SERIES=$out/frames.series \
  TERTIUS_X_FIRST_FILMED=1 TERTIUS_X_FROZEN=1 "${log[@]}" "$@" \
  nix develop --command bash tools/gpu-lock.sh --shared $out/lock.log $B/$build/probe-bin/Tertius.Demo \
  --script $out/script.json --headless --out $out --size 1920x1080 > $out/run.log 2>&1
echo "$(date +%T) $name exit $? $(wc -l < $out/tail.txt 2>/dev/null) draws logged, $(command ls $out/*.png 2>/dev/null | wc -l) pictures"
