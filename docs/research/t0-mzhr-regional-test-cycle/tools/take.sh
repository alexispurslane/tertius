#!/usr/bin/env bash
# take.sh <build x|m> <name> <parts> <cases,comma> [ENV=VALUE ...]: one survey run with the probe of <build> into
# /private/tmp/t0-mzhr/runs/<name>. The survey takes the shared GPU lock for each render (--chunk 0).
set -uo pipefail
build=$1; name=$2; parts=$3; cases=$4; shift 4
out=/private/tmp/t0-mzhr/runs/$name
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn "/private/tmp/t0-mzhr/out-$build/probe-bin" "$out/probe-bin"
echo "$build $parts $cases $*" > "$out/take.txt"
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-1f6ef865
echo "$(date +%T) start $name $build $parts $cases $*"
env "$@" nix develop --command python3 -I tools/renderer-survey.py run "$out" --parts "$parts" --only ${cases//,/ } --no-build --chunk 0 > "$out/survey.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(grep -vE '^\s|warning' "$out/survey.log" | tail -2 | cut -c1-200)"
