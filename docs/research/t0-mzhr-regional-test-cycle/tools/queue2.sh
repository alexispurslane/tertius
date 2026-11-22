#!/usr/bin/env bash
# The takes under today's jitter (18 phases). Names: t-<variant>-<take>; takes e (still at 4.08 stops), s (still at the
# automatic exposure), rb (film rect-bay), sw (cabin switch), lag (the three lag films). The region-4 still and switch
# takes are the control runs c-r8-* (the same build and switches).
T=/private/tmp/t0-mzhr/take.sh
CUT=TERTIUS_X_LIGHT_ACTION=cut
R4="$CUT TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10"
R18="$R4 TERTIUS_X_REGION_WINDOW=18"
C18="$CUT TERTIUS_X_CUT_LENGTH=18"
REF=TERTIUS_A_NO_CLAMP=1
E=TERTIUS_PROBE_EXPOSURE=4.08
$T x t-r18-e still rect-lamps $R18 $E
$T x t-r18-s still rect-lamps $R18
$T x t-ref-e still rect-lamps $REF $E
$T x t-ref-s still rect-lamps $REF
$T x t-fix-e still rect-lamps $CUT $E
$T x t-fix-s still rect-lamps $CUT
$T x t-r18-sw switch cabin $R18
$T x t-ref-sw switch cabin $REF
$T x t-fix-sw switch cabin $CUT
$T x t-r18-rb motion rect-bay $R18
$T x t-fix-rb motion rect-bay $CUT
$T x t-r4-rb motion rect-bay $R4
$T m t-main-rb motion rect-bay
$T m t-main-sw switch cabin
$T m t-main-e still rect-lamps $E
$T m t-main-s still rect-lamps
$T x t-c18-sw switch cabin $C18
$T x t-c18-rb motion rect-bay $C18
$T x t-c18-e still rect-lamps $C18 $E
$T x t-c18-s still rect-lamps $C18
$T m t-main-lag lag bay-sun,lamp-hall-16-moving,lamp-hall-64-spot-moving
$T x t-fix-lag lag bay-sun,lamp-hall-16-moving,lamp-hall-64-spot-moving $CUT
$T x t-c18-lag lag bay-sun,lamp-hall-16-moving,lamp-hall-64-spot-moving $C18
$T x t-r18-lag lag bay-sun,lamp-hall-16-moving,lamp-hall-64-spot-moving $R18
