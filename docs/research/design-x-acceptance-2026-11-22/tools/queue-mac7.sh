#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-mac7.sh: after queue-mac6.sh, the fair cabin switch (TERTIUS_PROBE_LAMP_GLOW=off) at the
# constant render scale 2/3 as pictures of all the video's frames (film "f" 140 to 459), on M and X, for the video and
# for the boil's measure where no size changes (macdyn.sh pictures, shared lock).
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd)
export BASE=/private/tmp/t0-5tyj
export KEEP=$(for k in $(seq 140 459); do printf 'f-%06d,' $k; done)
for side in m x; do
  [ -f $BASE/macdyn/pics/fair-$side-0.6667/f-000459.png ] || bash "$T/macdyn.sh" pictures switch $side 0.6667 fair-$side-0.6667
done
echo "$(date +%T) queue-mac7 done"
