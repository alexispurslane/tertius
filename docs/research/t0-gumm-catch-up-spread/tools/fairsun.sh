#!/usr/bin/env bash
# Tool code of t0-gumm. fairsun.sh <build> <name> [VAR=value...]: one Mac run of the gate's light change in the bay (the line
# orchestrator's order of 2026-11-22, 23:25), as fairrun.sh's bay (demo-surface's bay-camera after 420 ticks, the fixed
# exposure of -4.0 stops, 1920 x 1080 at the constant render scale 2/3, a frozen film) with the sun switched off at draw 200
# and on again at draw 400 (TERTIUS_X_SUN_OFF=200-400, scratch-sun-switch.patch: every light whose reach is at least 10^6 m).
# The film f has 600 frames; f-n is draw n. The bay start settles by draw 164, so the sun goes off on a settled picture, and
# 200 draws lie between the events. Builds xgy (today's cliff: light evidence restarts at 4 draws, with own rays) and xby (A,
# the cap: light evidence restarts at 8 draws, so no catch-up ray after a light change). It keeps the PNGs of draws 199 to
# 261 and 399 to 461, and writes frames.series, the size log and the own-ray log into /private/tmp/t0-gbgj/gumm-fair/<name>/.
set -uo pipefail
build=$1; name=$2; shift 2
N=$(cd "$(dirname "$0")/.." && pwd); R=$(cd "$N/../../.." && pwd)
B=/private/tmp/t0-gbgj; O=$B/gumm-fair; out=$O/$name; mkdir -p $O
f() { local p=$1 a=$2 b=$3; for ((i = a; i <= b; i++)); do printf '%s-%06d,' $p $i; done; }
keep="$(f f 199 261)$(f f 399 461)"
/bin/rm -rf $out; mkdir -p $out
python3 -I - > $out/script.json <<'PY'
import json, sys
steps = [{"do": "camera", "entity": "bay-camera"}, {"do": "run", "ticks": 420}]
steps += [{"do": "capture", "name": f"f-{n:06d}"} for n in range(600)]
json.dump({"scene": "demo-surface", "window": {"width": 1920, "height": 1080}, "steps": steps}, sys.stdout, indent=1)
PY
for ((d = 0; d < 605; d++)); do echo "$d 0 0 0.6667"; done > $out/replay.txt
echo "$(date +%T) $name: $build sun off 200-400 $*, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB"
cd "$R"
env TERTIUS_X_SUN_OFF=200-400 TERTIUS_PROBE_EXPOSURE=-4.0 TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_REPLAY=$out/replay.txt \
  TERTIUS_X_SIZE_LOG=$out TERTIUS_X_OWNRAY_LOG=$out TERTIUS_PROBE_KEEP=${keep%,} TERTIUS_PROBE_SERIES=$out/frames.series \
  TERTIUS_X_FIRST_FILMED=1 TERTIUS_X_FROZEN=1 "$@" \
  nix develop --command bash tools/gpu-lock.sh --shared $out/lock.log $B/bins/$build/probe-bin/Tertius.Demo \
  --script $out/script.json --headless --out $out --size 1920x1080 > $out/run.log 2>&1
echo "$(date +%T) $name exit $? $(wc -l < $out/size-*.txt 2>/dev/null) draws, $(ls $out/*.png 2>/dev/null | wc -l) pictures; $(grep -c 'PROBE draw .*: the sun' $out/run.log) sun lines"
