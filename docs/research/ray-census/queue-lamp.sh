#!/usr/bin/env bash
# Tool code of ter-8pco's lamp-hall part (for t7-3e22): the lamp hall at frame 62 of scripts/rect-lamps.json (the state
# of the speck films' bursts) at two fixed render sizes of main: 1072 x 603 (67 steps, bursts in the films) and
# 1040 x 585 (65 steps, the nearest size that one film held 10 or more frames without a burst). The fixed exposure is
# the films' 4.08 stops. The keep rectangle is the ceiling around the near lamps where the specks sit (output columns
# 643 to 1043, rows 200 to 400, the window of d16M-f62). Every capture's PNG is kept for a speck count.
set -u
E=/Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a-evidence/census
T=/private/tmp/ter-qt72/census-takes
cd /private/tmp/ter-qt72/census || exit 1
for pair in "0.558334 lamp-1072x603" "0.541668 lamp-1040x585"; do
  set -- $pair
  [ "$(/bin/df -g / | awk 'NR==2{print $4}')" -lt 20 ] && { echo "disk under 20 GiB: stop"; exit 2; }
  TERTIUS_PROBE_CENSUS_DUMP=1 CENSUS_EXPOSURE=4.08 CENSUS_KEEP_ALL=1 nix develop --command python3 -I $E/census.py /private/tmp/ter-qt72/census \
    rect-lamps-f62 "$1" "$T/$2" '643,200,1043,400' 2>&1 | tail -1
  nix develop --command python3 -I $E/lamp.py "$T/$2" > "$T/$2.lamp.txt" 2>&1; echo "lamp.py exit $?"
done
echo lamp queue done
# The exit cabin's take at 2/3 again: its first regions lay on windows, and its sums were deleted before the fix.
nix develop --command python3 -I $E/census.py /private/tmp/ter-qt72/census exit-cabin 0.6667 "$T/exit-cabin-067" 2>&1 | tail -1
echo exit retake done
