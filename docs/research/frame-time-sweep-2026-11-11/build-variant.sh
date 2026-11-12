#!/usr/bin/env bash
# Builds the Release demo of the sweep branch's commit $2 (default HEAD) with an optional python edit $3 <root>, into bin-$1. Dev shell.
set -euo pipefail
name=$1; commit=${2:-HEAD}; edit=${3:-}
S=/private/tmp/tertius-sweep-20261111
W=/Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0
src=$S/src-$name
rm -rf "$src" $S/bin-$name; mkdir -p "$src"
git -C $W archive "$commit" | tar -x -C "$src"
if [ -n "$edit" ]; then python3 -I "$edit" "$src"; fi
cd "$src" && dotnet build src/Tertius.Demo -c Release -o $S/bin-$name > $S/build-$name.log 2>&1 || { grep -E "error" $S/build-$name.log | head; exit 1; }
echo "built $name"
