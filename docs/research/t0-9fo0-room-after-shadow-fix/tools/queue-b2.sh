#!/usr/bin/env bash
# Tool code of t0-9fo0, part B. queue-b2.sh: the takes with the per-anchor region log (scratch-region-log.patch as it is
# in this record), on build x2 = 6d8a68332 with probe-acxm.patch and that patch. s-X2 is a plain still of x2, the
# control that x2 renders as x does. s-Xr2 is the still with the log, and g-Xr2 the grid film with the log.
T=$(cd "$(dirname "$0")" && pwd)
export BASE=/private/tmp/t0-9fo0b
X2=$BASE/x2/probe-bin
log() { echo "TERTIUS_X_REGION_LOG=$BASE/regionlog/$1"; }
PROBE=$X2 bash "$T/take.sh" s-X2 still rect-lamps
PROBE=$X2 bash "$T/take.sh" s-Xr2 still rect-lamps $(log s-Xr2)
PROBE=$X2 bash "$T/gridfilm.sh" g-Xr2 $(log g-Xr2)
echo "$(date +%T) queue-b2 done"
