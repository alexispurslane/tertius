#!/usr/bin/env bash
# scenetake.sh <name> [ENV=VALUE ...]: one survey still of rect-lamps in scene light with t0-y46f's scene.py into
# /private/tmp/t0-ion0/runs/<name>, with the scratch probe. A run with a long mean is kept.
set -uo pipefail
name=$1; shift
out=/private/tmp/t0-ion0/runs/$name
[ -e "$out/scene/rect-lamps-long.npy" ] && { echo "$(date +%T) kept $name"; exit 0; }
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn /private/tmp/t0-ion0/out-x/probe-bin "$out/probe-bin"
echo "scene $*" > "$out/take.txt"
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144
cd $R
echo "$(date +%T) start $name $*"
env "$@" nix develop --command python3 -I "$R/docs/research/t0-y46f-window-18-excess/tools/scene.py" "$out" rect-lamps > "$out/long.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(tail -1 "$out/long.log" | cut -c1-200)"
