#!/usr/bin/env bash
# Offline register experiment: compiles direct.frag and direct_key_fill.frag of the shader folder $2 under the tag $1
# for the M1's GPU and prints the highest register of each. No GPU work. Dev shell.
set -euo pipefail
tag=$1
shaders=${2:-/Users/alexispurslane/.t3/worktrees/tertius/t3-23bcaad4/src/Tertius.Rendering/Shaders}
out=/private/tmp/t2-mlvy/native/exp/$tag
rm -rf $out; mkdir -p $out
for f in direct direct_key_fill; do
  glslc --target-env=vulkan1.3 -O -Werror -I "$shaders" -o $out/$f.frag.spv "$shaders/$f.frag"
done
cd /private/tmp/t2-mlvy/native
python3 -I worktree.py $tag $out > /dev/null
python3 -I inspect_native.py $tag-direct $tag-keyfill | python3 -I /private/tmp/t2-mlvy/native/show.py $tag
