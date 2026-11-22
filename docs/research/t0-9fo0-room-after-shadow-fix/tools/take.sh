#!/usr/bin/env bash
# Tool code of t0-9fo0. take.sh <name> <parts> <cases,comma> [ENV=VALUE ...]: one survey run into $BASE/runs/<name>
# (BASE: /private/tmp/t0-9fo0 for part A, /private/tmp/t0-9fo0b for part B), through this checkout's
# tools/renderer-survey.py. The probe is PROBE. Without it, the probe is $BASE/out-x0/probe-bin, part A's X0 (the
# line's head with the survey's probe patch), and /private/tmp/t0-9fo0/out-m/probe-bin is part A's main (M). Part B
# has no out-x0, so its queues always set PROBE (builds-b.sh makes its builds). The survey takes the shared GPU lock for each render (--chunk 0).
# A run whose survey already wrote a result is kept, so that a stopped queue resumes.
set -uo pipefail
name=$1; parts=$2; cases=$3; shift 3
out=${BASE:-/private/tmp/t0-9fo0}/runs/$name
if compgen -G "$out/results/*/*.json" > /dev/null; then echo "$(date +%T) kept $name"; exit 0; fi
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn "${PROBE:-${BASE:-/private/tmp/t0-9fo0}/out-x0/probe-bin}" "$out/probe-bin"
echo "$parts $cases $* probe=$(readlink "$out/probe-bin")" > "$out/take.txt"
R=$(cd "$(dirname "$0")/../../../.." && pwd)
cd "$R"
echo "$(date +%T) start $name $parts $cases $*"
env "$@" nix develop --command python3 -I tools/renderer-survey.py run "$out" --parts "$parts" --only ${cases//,/ } --no-build --chunk 0 > "$out/survey.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(grep -vE '^\s|warning' "$out/survey.log" | tail -2 | cut -c1-200)"
