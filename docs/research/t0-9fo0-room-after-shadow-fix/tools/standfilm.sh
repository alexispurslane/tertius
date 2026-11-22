#!/usr/bin/env bash
# Tool code of t0-9fo0. standfilm.sh <name> [ENV=VALUE ...]: standfilm.py into $BASE/runs/<name> (as take.sh), with the
# probe of take.sh's rule (PROBE= for main). A run with a result is kept.
set -uo pipefail
name=$1; shift
out=${BASE:-/private/tmp/t0-9fo0}/runs/$name
if compgen -G "$out/results/*/*.json" > /dev/null; then echo "$(date +%T) kept $name"; exit 0; fi
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn "${PROBE:-${BASE:-/private/tmp/t0-9fo0}/out-x0/probe-bin}" "$out/probe-bin"
echo "standfilm $* probe=$(readlink "$out/probe-bin")" > "$out/take.txt"
R=$(cd "$(dirname "$0")/../../../.." && pwd)
cd $R
echo "$(date +%T) start $name standfilm $*"
env "$@" nix develop --command python3 -I $R/docs/research/t0-9fo0-room-after-shadow-fix/tools/standfilm.py "$out" > "$out/survey.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(tail -1 "$out/survey.log" | cut -c1-200)"
