#!/usr/bin/env bash
# earlytake.sh <name> [ENV=VALUE ...]: filmearly.py for the film rect-bay into /private/tmp/t0-ion0/runs/<name>, with
# the scratch probe (the fourth build unless PROBE names another). A run with a result is kept.
set -uo pipefail
name=$1; shift
out=/private/tmp/t0-ion0/runs/$name
[ -e "$out/results/early/rect-bay.json" ] && { echo "$(date +%T) kept $name"; exit 0; }
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn "${PROBE:-/private/tmp/t0-ion0/out-z/probe-bin}" "$out/probe-bin"
echo "early $*" > "$out/take.txt"
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144
cd $R
echo "$(date +%T) start $name $*"
env "$@" nix develop --command python3 -I "$R/docs/research/t0-ion0-region-drift/tools/filmearly.py" "$out" rect-bay > "$out/early.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(tail -1 "$out/early.log" | cut -c1-200)"
