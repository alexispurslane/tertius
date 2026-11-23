#!/usr/bin/env bash
# Tool code of t0-gbgj. scalefilm.sh <run name> <build name> [scale]: scalefilm.py into $W/runs/<run name> with the probe of
# $W/bins/<build name>, and TERTIUS_X_RENDER_SCALE=<scale> when a scale is given (without it, the headless 2/3). A run
# that kept its frames is kept.
set -uo pipefail
. "$(dirname "$0")/vars.sh"
name=$1; build=$2; scale=${3:-}
out=$W/runs/$name
if [ -e "$out/images/scale/cabin-455.png" ]; then echo "$(date +%T) kept $name"; exit 0; fi
mkdir -p "$out"; ln -sfn "$W/bins/$build/probe-bin" "$out/probe-bin"
echo "scalefilm.py $build scale=${scale:-2/3} probe=$(readlink "$out/probe-bin")" > "$out/take.txt"
R=$(git -C "$(dirname "$0")" rev-parse --show-toplevel); T=$(cd "$(dirname "$0")" && pwd)
cd "$R"
echo "$(date +%T) start $name $build scale ${scale:-2/3} (load $(sysctl -n vm.loadavg))"
env ${scale:+TERTIUS_X_RENDER_SCALE=$scale} nix develop --command python3 -I "$T/scalefilm.py" "$out" > "$out/survey.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(tail -1 "$out/survey.log" | cut -c1-200)"
