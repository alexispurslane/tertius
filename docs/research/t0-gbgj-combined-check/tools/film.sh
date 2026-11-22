#!/usr/bin/env bash
# Tool code of t0-gbgj. film.sh <run name> <case> <build name> [stops]: film.py into $W/runs/<run name> (as take.sh), with
# the probe of $W/bins/<build name>. A run with a result is kept.
set -uo pipefail
. "$(dirname "$0")/vars.sh"
name=$1; case=$2; build=$3; stops=${4:-}
out=$W/runs/$name
if compgen -G "$out/results/*/*.json" > /dev/null; then echo "$(date +%T) kept $name"; exit 0; fi
mkdir -p "$out"; ln -sfn "$W/bins/$build/probe-bin" "$out/probe-bin"
echo "film.py $build $case $stops probe=$(readlink "$out/probe-bin")" > "$out/take.txt"
R=$(git -C "$(dirname "$0")" rev-parse --show-toplevel); T=$(cd "$(dirname "$0")" && pwd)
cd "$R"
echo "$(date +%T) start $name film $case $build $stops (load $(sysctl -n vm.loadavg))"
nix develop --command python3 -I "$T/film.py" "$out" "$case" $stops > "$out/survey.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(tail -1 "$out/survey.log" | cut -c1-200)"
