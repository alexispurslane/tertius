#!/usr/bin/env bash
# take.sh <name> <parts> <cases,comma> [ENV=VALUE ...]: one survey run with the scratch probe into
# /private/tmp/t0-ion0/runs/<name>. The survey takes the shared GPU lock for each render (--chunk 0). In the environment,
# TERTIUS_X_LENGTHS=lengths becomes the run's folder lengths/ (the count of history lengths of t0-y46f), and
# TERTIUS_X_DRIFT_COUNT=drift becomes the run's folder drift/ (the count of the drift test, this ticket). A run whose
# survey already wrote a result or a switch series is kept, so that a stopped queue resumes.
set -uo pipefail
name=$1; parts=$2; cases=$3; shift 3
out=/private/tmp/t0-ion0/runs/$name
if compgen -G "$out/results/*/*.json" > /dev/null || compgen -G "$out/series/*/*.json" > /dev/null; then echo "$(date +%T) kept $name"; exit 0; fi
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn /private/tmp/t0-ion0/out-x/probe-bin "$out/probe-bin"
args=()
for a in "$@"; do
  [ "$a" = TERTIUS_X_LENGTHS=lengths ] && a="TERTIUS_X_LENGTHS=$out/lengths"
  [ "$a" = TERTIUS_X_DRIFT_COUNT=drift ] && a="TERTIUS_X_DRIFT_COUNT=$out/drift"
  args+=("$a")
done
echo "$parts $cases ${args[*]}" > "$out/take.txt"
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144
echo "$(date +%T) start $name $parts $cases ${args[*]}"
env "${args[@]}" nix develop --command python3 -I tools/renderer-survey.py run "$out" --parts "$parts" --only ${cases//,/ } --no-build --chunk 0 > "$out/survey.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(grep -vE '^\s|warning' "$out/survey.log" | tail -2 | cut -c1-200)"
