#!/usr/bin/env bash
# Builds the Release demo of commit $2 with a python edit script $3 applied to lighting.glsl, into bin-$1. Dev shell.
set -euo pipefail
name=$1; commit=$2; edit=$3
S=/private/tmp/t2-mlvy
src=$S/src-$name
rm -rf "$src" $S/bin-$name; mkdir -p "$src"
git -C /Users/alexispurslane/.t3/worktrees/tertius/t3-23bcaad4 archive "$commit" | tar -x -C "$src"
f=$src/src/Tertius.Rendering/Shaders/lighting.glsl
cp $f $f.orig; python3 -I "$edit" $f; diff $f.orig $f || true; rm $f.orig
cd "$src" && dotnet build src/Tertius.Demo -c Release -o $S/bin-$name > $S/build-$name.log 2>&1
echo "built $name"
