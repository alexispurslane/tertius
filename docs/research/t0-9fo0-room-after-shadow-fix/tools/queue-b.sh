#!/usr/bin/env bash
# Tool code of t0-9fo0, part B. queue-b.sh: the takes of part B in order, into /private/tmp/t0-9fo0b/runs. Builds by
# build.sh: X = 6d8a68332 with probe-acxm.patch and scratch-region-log.patch, X0 = 631cc61cc and M = d8559eb58, each with
# the survey probe of its own tree. M and X0 run again only where a picture or the chart needs their frames. Runs whose
# name ends in "r" carry the region log (TERTIUS_X_REGION_LOG) and repeat a plain run as its control.
T=$(cd "$(dirname "$0")" && pwd)
export BASE=/private/tmp/t0-9fo0b
X=$BASE/x/probe-bin; X0=$BASE/x0/probe-bin; M=$BASE/m/probe-bin
A="TERTIUS_A_NO_CLAMP=1"; E="TERTIUS_PROBE_EXPOSURE=4.08"
log() { echo "TERTIUS_X_REGION_LOG=$BASE/regionlog/$1"; }
take() { bash "$T/take.sh" "$@"; }
# Take 1: the still rect-lamps.
PROBE=$X take s-X still rect-lamps
PROBE=$X take e-X still rect-lamps $E
PROBE=$M take s-M still rect-lamps
PROBE=$X0 take s-X0 still rect-lamps
PROBE=$M take e-M still rect-lamps $E
PROBE=$X0 take e-X0 still rect-lamps $E
PROBE=$X take s-A still rect-lamps $A
PROBE=$X take e-A still rect-lamps $A $E
PROBE=$X take s-Xr still rect-lamps $(log s-Xr)
# Take 2: the switch cabin, and the grid films.
PROBE=$X take w-X switch cabin
PROBE=$M take w-M switch cabin
PROBE=$X0 take w-X0 switch cabin
PROBE=$X bash "$T/gridfilm.sh" g-Xr $(log g-Xr)
PROBE=$M bash "$T/gridfilm.sh" g-M
PROBE=$X0 bash "$T/gridfilm.sh" g-X0
# Take 3: the stand.
PROBE=$X take st-X stand cabin-stand
PROBE=$X bash "$T/standfilm.sh" sf-X
PROBE=$M bash "$T/standfilm.sh" sf-M
echo "$(date +%T) queue-b done"
