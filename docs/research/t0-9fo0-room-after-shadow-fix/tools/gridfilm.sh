#!/usr/bin/env bash
# Tool code of t0-9fo0, part B. gridfilm.sh <name> [ENV=VALUE ...]: gridfilm.py into $BASE/runs/<name>, with the probe
# of take.sh's rule (PROBE=). A run with its series is kept.
set -uo pipefail
name=$1; shift
out=${BASE:-/private/tmp/t0-9fo0}/runs/$name
if [ -e "$out/series/grid/cabin-switch.json" ]; then echo "$(date +%T) kept $name"; exit 0; fi
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn "${PROBE:?PROBE names the build}" "$out/probe-bin"
echo "gridfilm $* probe=$(readlink "$out/probe-bin")" > "$out/take.txt"
R=$(cd "$(dirname "$0")/../../../.." && pwd)
cd "$R"
echo "$(date +%T) start $name gridfilm $*"
env "$@" nix develop --command python3 -I "$R/docs/research/t0-9fo0-room-after-shadow-fix/tools/gridfilm.py" "$out" > "$out/survey.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(tail -1 "$out/survey.log" | cut -c1-200)"
