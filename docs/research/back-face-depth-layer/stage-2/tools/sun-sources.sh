#!/usr/bin/env bash
# Throwaway (t1-3j2j, part 5): path traces of rect-lamps' frame 239 with the sun alone and a black Earth, cropped to
# x 1300-1899, y 300-879, which holds the side wall, the jamb and the floor patch. Run in the assets shell
# (nix develop .#assets) from the worktree, under the shared GPU lock:
#   bash sun-sources.sh <export folder> <out folder> <samples>
# Variants: base (seeds 1 and 2), moon (the Moon, instance 2, drawn black), cap (a shadow-only cap 0.65 m above the hull's
# roof, seeds 1 and 2), cap+moon, rims (caps over the hull's outer edges alone). Each writes <out>/<variant>-s<seed>.npy.
# ONLY=<variant> runs that variant alone.
set -uo pipefail
export=$1; out=$2; samples=$3
T=docs/research/back-face-depth-layer/stage-2/tools
lock=/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh
mkdir -p "$out"
CAP=-11,11,6.5,-41,21
run() { # <name> <seed> [env assignments]
  local name=$1 seed=$2; shift 2
  [[ -n ${ONLY:-} && $ONLY != "$name" ]] && return
  env REF_EARTH_BLACK=1 REF_SKIP_LIGHTS=1,2,3,4,5 "$@" bash "$lock" --shared "$out/$name-s$seed.log" "$TERTIUS_BLENDER" -b \
    --factory-startup --python $T/cycles_scene.py -- "$export" "$out/$name-s$seed.exr" "$samples" "$seed" METAL \
    1300 300 1900 880 > /dev/null 2>&1
  "$TERTIUS_BLENDER" -b --factory-startup --python $T/exr_to_npy.py -- "$out/$name-s$seed.exr" "$out/$name-s$seed.npy" 2>&1 | grep NPY
  rm -f "$out/$name-s$seed.exr"
}
run base 1
run base 2
run moon 1 REF_BLACK=2
run cap 1 REF_SUN_CAP=$CAP
run cap 2 REF_SUN_CAP=$CAP
run capmoon 1 REF_SUN_CAP=$CAP REF_BLACK=2
# rims: caps 0.2 m wide over the hull's outer edges alone, which keep the sun from the lit lips of the openings and leave the
# roof and the outer faces lit.
run rims 1 "REF_SUN_CAP=9.9,10.1,6.5,-41,21;-10.1,-9.9,6.5,-41,21;-11,11,6.5,-40.1,-39.9"
