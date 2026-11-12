#!/usr/bin/env bash
# Builds main with the deck's exposure fixed and an optional lighting.glsl edit $2, or a root edit when $3 is "root", into bin-$1.
set -euo pipefail
name=$1; edit=${2:-}; kind=${3:-lighting}
S=/private/tmp/t2-mlvy
src=$S/src-$name
rm -rf "$src" $S/bin-$name; mkdir -p "$src"
git -C /Users/alexispurslane/.t3/worktrees/tertius/t3-23bcaad4 archive 8fe7ac7cf | tar -x -C "$src"
python3 -I -c "import sys; sys.path.insert(0, '$S/edits'); import fixexp; fixexp.apply('$src')"
if [ -n "$edit" ]; then
  if [ "$kind" = root ]; then python3 -I "$edit" "$src"; else python3 -I "$edit" "$src/src/Tertius.Rendering/Shaders/lighting.glsl"; fi
fi
cd "$src" && dotnet build src/Tertius.Demo -c Release -o $S/bin-$name > $S/build-$name.log 2>&1 || { grep -E "error" $S/build-$name.log | head; exit 1; }
echo "built $name"
