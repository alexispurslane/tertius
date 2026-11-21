#!/usr/bin/env bash
# Tool code of t0-xdck. round.sh <round> <side...>: one timing round of the film rect-bay. It takes the exclusive GPU
# lock once (tools/gpu-lock.sh without --shared) and runs round-inner.sh under it, which runs each side alone, one after
# the other, beside gpu-clock (tools/gpu-clock.sh). The rounds rotate the order of the sides. A round of seven sides holds
# the lock about 1 to 1.5 minutes. Output: /private/tmp/t0-xdck/timing/<round>-<side>/ per side, and <round>.log.
set -uo pipefail
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144
D=$R/docs/research/t0-xdck-drift-cut-cost/tools
round=$1; shift
mkdir -p /private/tmp/t0-xdck/timing
cd $R
echo "$(date +%T) start round $round: $*"
nix develop --command bash tools/gpu-lock.sh /private/tmp/t0-xdck/timing/$round.log bash $D/round-inner.sh "$round" "$@"
echo "$(date +%T) end round $round exit $?"
