#!/usr/bin/env bash
# variant.sh <tag> <edit.py>: copies the worktree's shaders, applies the edit to the copy, and prints the registers.
set -euo pipefail
tag=$1; edit=$2
d=/private/tmp/t2-mlvy/native/var/$tag
rm -rf $d; cp -R /Users/alexispurslane/.t3/worktrees/tertius/t3-23bcaad4/src/Tertius.Rendering/Shaders $d
python3 -I $edit $d
bash "$(dirname "$0")/registers.sh" $tag $d
