#!/usr/bin/env bash
# Builds the Release demo of main's commit 8fe7ac7cf with a python edit script $2 <root> applied, into bin-$1. Dev shell.
set -euo pipefail
name=$1; edit=$2
S=/private/tmp/t2-mlvy
src=$S/src-$name
rm -rf "$src" $S/bin-$name; mkdir -p "$src"
git -C /Users/alexispurslane/.t3/worktrees/tertius/t3-23bcaad4 archive 8fe7ac7cf | tar -x -C "$src"
python3 -I "$edit" "$src"
cd "$src" && dotnet build src/Tertius.Demo -c Release -o $S/bin-$name > $S/build-$name.log 2>&1 || { grep -E "error" $S/build-$name.log | head; exit 1; }
echo "built $name"
