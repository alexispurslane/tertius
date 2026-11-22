#!/usr/bin/env bash
# The takes under the jitter TERTIUS_DEBUG_JITTER=none. Names: n-<variant>-<take>, as in queue2.sh.
T=/private/tmp/t0-mzhr/take.sh
J=TERTIUS_DEBUG_JITTER=none
CUT="$J TERTIUS_X_LIGHT_ACTION=cut"
R4="$CUT TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10"
R18="$R4 TERTIUS_X_REGION_WINDOW=18"
REF="$J TERTIUS_A_NO_CLAMP=1"
E=TERTIUS_PROBE_EXPOSURE=4.08
$T x n-ref-e still rect-lamps $REF $E
$T x n-fix-e still rect-lamps $CUT $E
$T x n-r4-e still rect-lamps $R4 $E
$T x n-r18-e still rect-lamps $R18 $E
$T x n-ref-s still rect-lamps $REF
$T x n-fix-s still rect-lamps $CUT
$T x n-r4-s still rect-lamps $R4
$T x n-r18-s still rect-lamps $R18
$T x n-ref-sw switch cabin $REF
$T x n-fix-sw switch cabin $CUT
$T x n-r4-sw switch cabin $R4
$T x n-r18-sw switch cabin $R18
$T x n-fix-rb motion rect-bay $CUT
$T x n-r4-rb motion rect-bay $R4
$T x n-r18-rb motion rect-bay $R18
