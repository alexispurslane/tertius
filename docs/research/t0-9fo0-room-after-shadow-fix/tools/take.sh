#!/usr/bin/env bash
# Tool code of t0-9fo0. take.sh <name> <parts> <cases,comma> [ENV=VALUE ...]: one survey run into
# /private/tmp/t0-9fo0/runs/<name>, through this checkout's tools/renderer-survey.py. The probe is X0's, the line's head
# with the survey's probe patch (/private/tmp/t0-9fo0/out-x0), unless PROBE names another, for example
# /private/tmp/t0-9fo0/out-m/probe-bin for main (M). The survey takes the shared GPU lock for each render (--chunk 0).
# A run whose survey already wrote a result is kept, so that a stopped queue resumes.
set -uo pipefail
name=$1; parts=$2; cases=$3; shift 3
out=/private/tmp/t0-9fo0/runs/$name
if compgen -G "$out/results/*/*.json" > /dev/null; then echo "$(date +%T) kept $name"; exit 0; fi
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn "${PROBE:-/private/tmp/t0-9fo0/out-x0/probe-bin}" "$out/probe-bin"
echo "$parts $cases $* probe=$(readlink "$out/probe-bin")" > "$out/take.txt"
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144
echo "$(date +%T) start $name $parts $cases $*"
env "$@" nix develop --command python3 -I tools/renderer-survey.py run "$out" --parts "$parts" --only ${cases//,/ } --no-build --chunk 0 > "$out/survey.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(grep -vE '^\s|warning' "$out/survey.log" | tail -2 | cut -c1-200)"
