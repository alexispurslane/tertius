#!/usr/bin/env bash
# scenetake.sh <name> [ENV=VALUE ...]: one survey still of rect-lamps in scene light, with scene.py of this
# folder, into /private/tmp/t8-69v7/runs/<name>, with the scratch probe. TERTIUS_X_STAGES=stages becomes the run's
# stages/ folder (the stage means, stages.py).
set -uo pipefail
name=$1; shift
root=/Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59
out=/private/tmp/t8-69v7/runs/$name
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn /private/tmp/t8-69v7/out-x/probe-bin "$out/probe-bin"
args=()
for a in "$@"; do [ "$a" = TERTIUS_X_STAGES=stages ] && a="TERTIUS_X_STAGES=$out/stages"; args+=("$a"); done
echo "scene ${args[*]}" > "$out/take.txt"
cd "$root"
echo "$(date +%T) start $name ${args[*]}"
env "${args[@]}" nix develop --command python3 -I docs/research/t8-69v7-length-light/tools/scene.py "$out" rect-lamps > "$out/scene.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(tail -1 "$out/scene.log" | cut -c1-200)"
