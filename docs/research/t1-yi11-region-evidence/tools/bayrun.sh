#!/usr/bin/env bash
# Tool code of t1-yi11. bayrun.sh <build> <name> [VAR=value...]: the frozen bay (the survey's still bay-sunbeam:
# demo-surface's bay-camera after 420 ticks) with the probe build /private/tmp/t1-yi11/<build>/probe-bin (builds.sh), at
# 1920 x 1080 and the headless render scale 2/3 (1280 x 720), at the back-face line's fixed exposure of -4.089 stops, into
# /private/tmp/t1-yi11/runs/<name>/. A frozen film of 660 draws from the start (TERTIUS_X_FROZEN=1, TERTIUS_X_FIRST_FILMED=1:
# every capture draws once with no step of time, so f-n is draw n); it keeps f-000640 to f-000659 and dumps the slow
# diffuse history's length at draw 639 (TERTIUS_X_DUMP_AT). The extra VAR=value words go into its environment: the
# columns' knobs (TERTIUS_X_DIFFUSE_CAP, TERTIUS_X_REGION_OFF) and TERTIUS_PROBE_SIGNAL=2 for the diffuse signal alone.
# DRAWS=<n> (default 660) and EVERY=<k> (default 1) change the take: it keeps every k-th draw of the last 20 * k draws
# and dumps at the draw before the first kept one. KEEP_FROM=<n> keeps every draw from n instead (a frozen film), and
# the dump stays at draw draws - 21. KEEP_LIST=<n,m,...> keeps only those draws (sparse, to save disk), with the
# dump at draw draws - 21 too. Bloom stays on, as the game draws it. It holds the GPU lock in its shared mode and refuses to start under 20 GiB free.
set -uo pipefail
build=$1; name=$2; shift 2
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd)
B=/private/tmp/t1-yi11; out=$B/runs/$name
free=$(df -k / | awk 'NR == 2 {printf "%d", $4 / 1048576}')
if [ "$free" -lt 20 ]; then echo "$(date +%T) $name: only $free GiB free, under the line's stop of 20: no run"; exit 1; fi
command rm -rf "${out:?}"; mkdir -p $out
draws=${DRAWS:-660}; every=${EVERY:-1}; first=$((draws - 20 * every))
python3 -I - $draws > $out/script.json <<'PY'
import json, sys
steps = [{"do": "camera", "entity": "bay-camera"}, {"do": "run", "ticks": 420}]
steps += [{"do": "capture", "name": f"f-{n:06d}"} for n in range(int(sys.argv[1]))]
json.dump({"scene": "demo-surface", "window": {"width": 1920, "height": 1080}, "steps": steps}, sys.stdout, indent=1)
PY
keep=$(seq -f 'f-%06g' ${KEEP_FROM:-$first} $([ -n "${KEEP_FROM:-}" ] && echo 1 || echo $every) $((draws - 1)) | paste -sd, -)
[ -n "${KEEP_LIST:-}" ] && keep=$(echo "$KEEP_LIST" | tr ',' '\n' | xargs printf 'f-%06d\n' | paste -sd, -)
[ -n "${KEEP_LIST:-}" ] && first=$((draws - 20))
echo "$(date +%T) $name: $build $*, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB, load $(sysctl -n vm.loadavg | awk '{print $2}')"
cd "$R"
env TERTIUS_PROBE_EXPOSURE=-4.089 TERTIUS_PROBE_KEEP=$keep TERTIUS_PROBE_SERIES=$out/frames.series \
  TERTIUS_X_FIRST_FILMED=1 TERTIUS_X_FROZEN=1 TERTIUS_X_DUMP_DIFFUSE=$out TERTIUS_X_DUMP_AT=$((first - 1)) "$@" \
  nix develop --command bash tools/gpu-lock.sh --shared $out/lock.log $B/$build/probe-bin/Tertius.Demo \
  --script $out/script.json --headless --out $out --size 1920x1080 > $out/run.log 2>&1
echo "$(date +%T) $name exit $? $(command ls $out/*.png 2>/dev/null | wc -l) pictures, dump $(command ls $out/*.f32 2>/dev/null | xargs -n1 basename 2>/dev/null)"
