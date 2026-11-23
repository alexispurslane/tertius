#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-mac8.sh: after queue-mac7.sh, what lights the cabin's window jambs at the render scale
# 0.956 and not at 2/3 (macdyn.sh pictures of the fair switch, shared lock, film frames 150 and 355: 10 draws before
# the lamps go off and 195 draws after):
#   1. M at a constant 0.956 with the sun off (TERTIUS_PROBE_SUN=off): does the sun light the jambs?
#   2. M and the clamps-off reference A (X with TERTIUS_A_NO_CLAMP=1) at the constant scales 0.70, 0.80, 0.90 and 1.0,
#      beside the existing 2/3 and 0.956 runs: at which scale does the jambs' light appear?
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd)
export BASE=/private/tmp/t0-5tyj
export KEEP=f-000150,f-000355
bash "$T/macdyn.sh" pictures switch m 0.956 sun-m-0.956 TERTIUS_PROBE_SUN=off
for scale in 0.70 0.80 0.90 1.0; do
  bash "$T/macdyn.sh" pictures switch m $scale sweep-m-$scale
  bash "$T/macdyn.sh" pictures switch x $scale sweep-a-$scale TERTIUS_A_NO_CLAMP=1
done
echo "$(date +%T) queue-mac8 done"
