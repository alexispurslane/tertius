#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-final.sh: the rows of the per-scene table that the user's bar rests on, taken again on the
# merged code (t0-5tyj, 2026-11-22), and take T17. Sides: XF is the build "fin" (playbuild.sh x with PATCHSET=step2 at
# the acceptance branch 16d915a67: Design X with the cap, the fix of tc-8142 and main's step 2), MF the probe build of main
# 3549d3283 with its own probe patch, AF the build "fin" with both clamps off (TERTIUS_A_NO_CLAMP=1, the reference of the
# daub and the light share). One heavy command at a time, M before X in each pair, as queue-mac.sh did:
#   1. the builds mfin (MF), tm2 and tx2 (plain Release demos of main 3549d3283 and of the branch's head, for T17);
#   2. the stills of the light share and the daub (rect-lamps, bay-sunbeam, docking-bay) and of the cabins, on MF and XF;
#      the three light stills on AF; the light share's fixed exposures on all three sides;
#   3. the switches bay-sun-off and lamp-hall-16, and the fair cabin switch on MF (XF's is queue-fin.sh's fair-FIN);
#   4. the bay films rect-bay and bay-sunbeam;
#   5. take T17 (timing.sh with MAIN=tm2, BUILD=tx2, OUT=t17f), each round when the load is under 8.
# A run whose survey already wrote a result is kept (take.sh), so that a stopped queue resumes.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd)
R=$(cd "$T/../../../.." && pwd)
export BASE=/private/tmp/t0-5tyj; B=$BASE
X=$B/finplay/probe-bin; M=$B/mfin/probe-bin
A="TERTIUS_A_NO_CLAMP=1"
head=$(git -C "$R" rev-parse HEAD)
[ -x $M/Tertius.Demo ] || bash "$T/build.sh" mfin 3549d3283 3549d3283:tools/renderer-survey/probe.patch
[ -x $B/tm2/probe-bin/Tertius.Demo ] || bash "$T/build.sh" tm2 3549d3283
[ -x $B/tx2/probe-bin/Tertius.Demo ] || bash "$T/build.sh" tx2 "$head"
take() { bash "$T/take.sh" "$@"; }
pair() { local name=$1; shift; PROBE=$M take "$name-MF" "$@"; PROBE=$X take "$name-XF" "$@"; }
echo "$(date +%T) stills"
pair sF still rect-lamps,bay-sunbeam,docking-bay
pair cF still cabin-light-cabin,docking-cabin,exit-cabin,station-cabin,surface-cabin,box-cabin,windows-cabin
PROBE=$X take s-AF still rect-lamps,bay-sunbeam,docking-bay $A
for side in MF XF AF; do
  probe=$X; [ $side = MF ] && probe=$M; env=""; [ $side = AF ] && env=$A
  PROBE=$probe take "e-lamps-$side" still rect-lamps TERTIUS_PROBE_EXPOSURE=4.08 $env
  PROBE=$probe take "e-bay-$side" still bay-sunbeam,docking-bay TERTIUS_PROBE_EXPOSURE=-4.0 $env
done
echo "$(date +%T) switches"
pair wF switch bay-sun-off,lamp-hall-16
PROBE=$M take fair-MF switch cabin
echo "$(date +%T) films"
pair fF motion rect-bay,bay-sunbeam
echo "$(date +%T) T17"
MAIN=tm2 BUILD=tx2 OUT=t17f bash "$T/timing.sh"
echo "$(date +%T) queue-final done"
