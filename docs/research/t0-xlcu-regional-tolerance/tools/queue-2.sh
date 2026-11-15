#!/usr/bin/env bash
# Queue 2, step 2: each rule acting, on the build with T3. Names: <jitter t|n>-<variant>-<take>. Variants: A the
# reference (clamps off), fix (light evidence cuts, wide box), T0 (region, window 18, corrected window, 10 %), T1 and T2
# (T0 with the derived rule), cap4 (the fix with the bounce history held at 4 draws, no regional test). Takes: e the lamp
# hall still at 4.08 stops with the count of lengths, s the same still at the automatic exposure, sw the cabin switch,
# rb the film rect-bay, lag the lag film bay-sun. Each variable is named as its variant, so that ${!v} gives its switches. t-A-e, t-T0-e, n-A-e and n-T0-e are the control runs of queue c.
T=$(dirname "$0")/take.sh
J=TERTIUS_DEBUG_JITTER=none; E=TERTIUS_PROBE_EXPOSURE=4.08; L="TERTIUS_X_LENGTHS=lengths TERTIUS_X_LENGTHS_EVERY=7"
A=TERTIUS_A_NO_CLAMP=1
fix=TERTIUS_X_LIGHT_ACTION=cut
T0="$fix TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1"
T1="$T0 TERTIUS_X_REGION_RULE=1"; T2="$T0 TERTIUS_X_REGION_RULE=2"
cap4="$fix TERTIUS_X_DIFFUSE_CAP=4"
for j in t n; do
  JJ=$([ $j = n ] && echo $J)
  $T $j-fix-e still rect-lamps $JJ $fix $E $L
  $T $j-T1-e still rect-lamps $JJ $T1 $E $L
  $T $j-T2-e still rect-lamps $JJ $T2 $E $L
  $T $j-A-s still rect-lamps $JJ $A
  $T $j-fix-s still rect-lamps $JJ $fix
  $T $j-T0-s still rect-lamps $JJ $T0
  $T $j-T1-s still rect-lamps $JJ $T1
  $T $j-T2-s still rect-lamps $JJ $T2
done
for v in A fix T0 T1 T2 cap4; do $T t-$v-sw switch cabin ${!v}; done
for v in A fix T0 T1 T2; do $T n-$v-sw switch cabin $J ${!v}; done
for v in fix T0 T1 T2 cap4; do $T t-$v-rb motion rect-bay ${!v}; done
for v in fix T0 T1 T2; do $T t-$v-lag lag bay-sun ${!v}; done
