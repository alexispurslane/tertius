#!/usr/bin/env bash
# Tool code of t0-9fo0. queue-3.sh: the census without the area rule, on the scratch build of X0 with
# ../scratch-no-area-rule.patch (/private/tmp/t0-9fo0/na/probe-bin), with and without jitter.
T=$(dirname "$0"); source "$T/vars.sh"; take() { bash "$T/take.sh" "$@"; }
export PROBE=/private/tmp/t0-9fo0/na/probe-bin
take na-X0 census-still rect-lamps
take naj-X0 census-still rect-lamps $NJ
# The same with jitter, keeping each draw's block map (TERTIUS_X_CENSUS_EVERY=1), to find where the young records lie.
take nm-X0 census-still rect-lamps TERTIUS_X_CENSUS_EVERY=1
# The same with the sun off (the probe's TERTIUS_PROBE_SUN=off), to tell the sun's records from the lamps'.
take ns-X0 census-still rect-lamps TERTIUS_X_CENSUS_EVERY=1 TERTIUS_PROBE_SUN=off
