#!/usr/bin/env bash
# longtake.sh <name> <captures> [ENV=VALUE ...]: one long still of rect-lamps with long.py into
# /private/tmp/t0-y46f/runs/<name>, with the scratch probe. TERTIUS_X_LENGTHS=lengths becomes the run's lengths/ folder.
set -uo pipefail
name=$1; count=$2; shift 2
out=/private/tmp/t0-y46f/runs/$name
mkdir -p "$out"; [ -e "$out/probe-bin" ] || ln -sfn /private/tmp/t0-y46f/out-x/probe-bin "$out/probe-bin"
args=()
for a in "$@"; do [ "$a" = TERTIUS_X_LENGTHS=lengths ] && a="TERTIUS_X_LENGTHS=$out/lengths"; args+=("$a"); done
echo "long $count ${args[*]}" > "$out/take.txt"
here=$(cd "$(dirname "$0")" && pwd)
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59
echo "$(date +%T) start $name $count ${args[*]}"
env "${args[@]}" nix develop --command python3 -I "$here/long.py" "$out" rect-lamps "$count" > "$out/long.log" 2>&1
rc=$?
echo "$(date +%T) end $name exit $rc $(tail -1 "$out/long.log" | cut -c1-200)"
