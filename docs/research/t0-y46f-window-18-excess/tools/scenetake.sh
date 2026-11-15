#!/usr/bin/env bash
# scenetake.sh <name> [ENV=VALUE ...]: one survey still of rect-lamps in scene light with scene.py into
# /private/tmp/t0-y46f/runs/<name>, with the scratch probe. TERTIUS_X_LENGTHS=lengths becomes the run's lengths/ folder.
set -uo pipefail
name=$1; shift
out=/private/tmp/t0-y46f/runs/$name
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn /private/tmp/t0-y46f/out-x/probe-bin "$out/probe-bin"
args=()
for a in "$@"; do [ "$a" = TERTIUS_X_LENGTHS=lengths ] && a="TERTIUS_X_LENGTHS=$out/lengths"; args+=("$a"); done
echo "scene ${args[*]}" > "$out/take.txt"
here=$(cd "$(dirname "$0")" && pwd)
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59
echo "$(date +%T) start $name ${args[*]}"
env "${args[@]}" nix develop --command python3 -I "$here/scene.py" "$out" rect-lamps > "$out/long.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(tail -1 "$out/long.log" | cut -c1-200)"
