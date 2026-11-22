#!/usr/bin/env bash
# Tool code of t0-acxm. take.sh <run name> <parts> <cases,comma> <side>: one survey run of the side (vars.sh) into
# $W/runs/<run name>, with the side's probe and switches. The survey takes the shared GPU lock for each render
# (--chunk 0). A run whose survey already wrote a result is kept, so that a stopped queue resumes.
set -uo pipefail
. "$(dirname "$0")/vars.sh"
name=$1; parts=$2; cases=$3; side=$4
read -r probe envs <<< "${!side}"
out=$W/runs/$name
if compgen -G "$out/results/*/*.json" > /dev/null; then echo "$(date +%T) kept $name"; exit 0; fi
mkdir -p "$out"; ln -sfn "$W/bins/$probe/probe-bin" "$out/probe-bin"
echo "$side $parts $cases $envs probe=$(readlink "$out/probe-bin")" > "$out/take.txt"
cd "$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
echo "$(date +%T) start $name $side $parts $cases $envs (load $(sysctl -n vm.loadavg))"
env $envs nix develop --command python3 -I tools/renderer-survey.py run "$out" --parts "$parts" --only ${cases//,/ } --no-build --chunk 0 > "$out/survey.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(grep -vE '^\s|warning' "$out/survey.log" | tail -2 | cut -c1-200)"
