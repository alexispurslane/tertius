#!/usr/bin/env bash
# Tool code of t0-xdck. take.sh <name> <parts> <cases,comma> [ENV=VALUE ...]: one survey run into
# /private/tmp/t0-xdck/runs/<name>. The probe is the scratch build out-x (the t0-ion0 patch with this ticket's switches)
# unless PROBE names another, for example /private/tmp/t0-xdck/out-m/probe-bin for main (M). The survey takes the shared
# GPU lock for each render (--chunk 0). TERTIUS_X_OWN_COUNT=own becomes the run's folder own/ (the own-ray count). A run
# whose survey already wrote a result is kept, so that a stopped queue resumes.
set -uo pipefail
name=$1; parts=$2; cases=$3; shift 3
out=/private/tmp/t0-xdck/runs/$name
if compgen -G "$out/results/*/*.json" > /dev/null; then echo "$(date +%T) kept $name"; exit 0; fi
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn "${PROBE:-/private/tmp/t0-xdck/out-x/probe-bin}" "$out/probe-bin"
args=()
for a in "$@"; do
  [ "$a" = TERTIUS_X_OWN_COUNT=own ] && a="TERTIUS_X_OWN_COUNT=$out/own"
  args+=("$a")
done
echo "$parts $cases ${args[*]} probe=$(readlink "$out/probe-bin")" > "$out/take.txt"
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144
echo "$(date +%T) start $name $parts $cases ${args[*]}"
env "${args[@]}" nix develop --command python3 -I tools/renderer-survey.py run "$out" --parts "$parts" --only ${cases//,/ } --no-build --chunk 0 > "$out/survey.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(grep -vE '^\s|warning' "$out/survey.log" | tail -2 | cut -c1-200)"
