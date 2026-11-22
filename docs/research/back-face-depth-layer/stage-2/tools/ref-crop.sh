#!/usr/bin/env bash
# Throwaway (t1-3j2j): one path-traced crop of an exported frame with a black Earth, for parts 1, 3 and 4.
# Run in the assets shell (nix develop .#assets) from the worktree, under the shared GPU lock:
#   bash ref-crop.sh <export folder> <out stem> <samples> <seed> <skipped lights> x0 y0 x1 y1
# REF_EARTH_BLACK=0 in the environment keeps Earth lit. <skipped lights> is REF_SKIP_LIGHTS: the comma-separated
# indices of the scene's lights to drop, or "" to keep all. It writes <out stem>.exr and <out stem>.npy.
# The GPU lock is tools/gpu-lock.sh, or $GPU_LOCK.
# The runs of the record, with E=/private/tmp/t1-3j2j/export and R=/private/tmp/t1-3j2j/ref:
#   part 1:  $E/rect-lamps $R/rl-jamb 256 1 1,2,3,4,5 1756 470 1816 610
#            $E/rect-lamps $R/rl-sill 256 1 1,2,3,4,5 880 552 1040 576
#            $E/cabin $R/cabin-hub 256 1 0,1,2,...,20 1500 480 1800 720 (every light but the sun)
#   part 3:  REF_EARTH_BLACK=0, $E/box $R/box-edges 64 1 "" 600 575 960 655
#   part 4:  REF_EARTH_BLACK=0, $E/orbit $R/orbit-earth 64 1 "" 1340 900 1460 980
set -uo pipefail
export=$1; out=$2; samples=$3; seed=$4; skip=$5; shift 5
T=docs/research/back-face-depth-layer/stage-2/tools
REF_EARTH_BLACK=${REF_EARTH_BLACK:-1} REF_SKIP_LIGHTS=$skip bash "${GPU_LOCK:-tools/gpu-lock.sh}" --shared "$out.log" \
  "$TERTIUS_BLENDER" -b --factory-startup --python $T/cycles_scene.py -- "$export" "$out.exr" "$samples" "$seed" METAL "$@" \
  > /dev/null 2>&1
"$TERTIUS_BLENDER" -b --factory-startup --python $T/exr_to_npy.py -- "$out.exr" "$out.npy" 2>&1 | grep NPY
