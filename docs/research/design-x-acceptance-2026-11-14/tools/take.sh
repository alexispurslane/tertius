#!/usr/bin/env bash
# take.sh <tree x|m> <suffix> <parts> <cases,comma> [ENV=VALUE ...]: one survey run of the branch's tool with the probe of <tree>,
# into runs/out-<tree><suffix>. The survey takes the shared GPU lock for each render.
set -uo pipefail
tree=$1; suffix=$2; parts=$3; cases=$4; shift 4
out=/private/tmp/t7-3e22/x2/runs/out-$tree$suffix
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn "/private/tmp/t7-3e22/x2/out-$tree/probe-bin" "$out/probe-bin"
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-09c31e02
echo "$(date +%T) start $out $parts $cases $*"
env "$@" nix develop --command python3 -I tools/renderer-survey.py run "$out" --parts "$parts" --only ${cases//,/ } --no-build --chunk 0 2>&1 | grep -vE '^\s|warning' | cut -c1-300 | tail -6
echo "$(date +%T) end $out $parts"
