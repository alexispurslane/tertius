#!/usr/bin/env bash
# take.sh <name> <parts> <cases,comma> [ENV=VALUE ...]: one survey run with the scratch probe into
# /private/tmp/t0-y46f/runs/<name>. The survey takes the shared GPU lock for each render (--chunk 0). If the
# environment holds TERTIUS_X_LENGTHS=lengths, the count of history lengths goes to the run's folder lengths/.
set -uo pipefail
name=$1; parts=$2; cases=$3; shift 3
out=/private/tmp/t0-y46f/runs/$name
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn /private/tmp/t0-y46f/out-x/probe-bin "$out/probe-bin"
args=()
for a in "$@"; do [ "$a" = TERTIUS_X_LENGTHS=lengths ] && a="TERTIUS_X_LENGTHS=$out/lengths"; args+=("$a"); done
echo "$parts $cases ${args[*]}" > "$out/take.txt"
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59
echo "$(date +%T) start $name $parts $cases ${args[*]}"
env "${args[@]}" nix develop --command python3 -I tools/renderer-survey.py run "$out" --parts "$parts" --only ${cases//,/ } --no-build --chunk 0 > "$out/survey.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(grep -vE '^\s|warning' "$out/survey.log" | tail -2 | cut -c1-200)"
