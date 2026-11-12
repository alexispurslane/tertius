#!/usr/bin/env bash
# Builds the Release demo of the worktree as it is, with an optional python edit $2 <root>, into bin-$1. Dev shell.
set -euo pipefail
name=$1; edit=${2:-}
S=/private/tmp/t2-mlvy
W=/Users/alexispurslane/.t3/worktrees/tertius/t3-23bcaad4
src=$S/src-$name
rm -rf "$src" $S/bin-$name; mkdir -p "$src"
(cd $W && git ls-files -z --cached --others --exclude-standard | rsync -a --from0 --files-from=- . "$src")
if [ -n "$edit" ]; then python3 -I "$edit" "$src"; fi
cd "$src" && dotnet build src/Tertius.Demo -c Release -o $S/bin-$name > $S/build-$name.log 2>&1 || { grep -E "error" $S/build-$name.log | head; exit 1; }
echo "built $name"
