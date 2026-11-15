# vars.sh: the variants of step 2, sourced by the queues. Every variant has the nearest-half store (HN). Each variable is
# named as its variant, so that ${!v} gives its switches.
#   A     the reference: clamps off.
#   fix   light evidence cuts the history to the fast cap and leaves the box wide.
#   cap4  the fix with the bounce history held at 4 draws.
#   Q0    the fix with the drift test acting: evidence where the drift is above 0.10.
#   Q1    the same, evidence where the drift is above 2 standard deviations of Rnew - Rold.
#   Q3    Q1 at 3 standard deviations (step 1 named it Q2; the ticket gave the name Q2 to the next rule).
#   Q2    the fix with the drift test acting: evidence where the mean of the last m differences at equal jitter phase,
#         F(t) - F(t - 18), is above 2 standard deviations of that mean, for any m of 3, 6 and 18.
# The drift test reads the 18-draw window ring, so its variants turn the ring on. No variant has the regional lag test.
HN=TERTIUS_X_HALF_NEAREST=1
A="TERTIUS_A_NO_CLAMP=1 $HN"
fix="TERTIUS_X_LIGHT_ACTION=cut $HN"
cap4="$fix TERTIUS_X_DIFFUSE_CAP=4"
drift="$fix TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1 TERTIUS_X_DRIFT=8"
Q0="$drift TERTIUS_X_DRIFT_RULE=0"
Q1="$drift TERTIUS_X_DRIFT_RULE=1"
Q3="$Q1 TERTIUS_X_DRIFT_SIGMAS=3"
Q2="$drift TERTIUS_X_DRIFT_RULE=2"
E=TERTIUS_PROBE_EXPOSURE=4.08
L="TERTIUS_X_LENGTHS=lengths TERTIUS_X_LENGTHS_EVERY=7"
