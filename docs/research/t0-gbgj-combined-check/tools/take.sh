#!/usr/bin/env bash
# Tool code of t0-gbgj. take.sh <run name> <parts> <cases,comma> <build name> [ENV=VALUE ...]: one survey run into
# $W/runs/<run name>, with the probe of $W/bins/<build name>, through this checkout's tools/renderer-survey.py. The survey
# takes the shared GPU lock for each render (--chunk 0). A run whose survey already wrote a result is kept, so that a
# stopped queue resumes.
set -uo pipefail
. "$(dirname "$0")/vars.sh"
name=$1; parts=$2; cases=$3; build=$4; shift 4
out=$W/runs/$name
if compgen -G "$out/results/*/*.json" > /dev/null; then echo "$(date +%T) kept $name"; exit 0; fi
mkdir -p "$out"; ln -sfn "$W/bins/$build/probe-bin" "$out/probe-bin"
echo "$build $parts $cases $* probe=$(readlink "$out/probe-bin")" > "$out/take.txt"
cd "$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
echo "$(date +%T) start $name $build $parts $cases $* (load $(sysctl -n vm.loadavg))"
env "$@" nix develop --command python3 -I tools/renderer-survey.py run "$out" --parts "$parts" --only ${cases//,/ } \
  --no-build --chunk 0 > "$out/survey.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(grep -vE '^\s|warning' "$out/survey.log" | tail -2 | cut -c1-200)"
