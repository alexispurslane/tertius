# Tool code of t0-xdck. vars.sh: the variants, sourced by the queues. Each variable is named as its variant, so that ${!v}
# gives its switches. Every probe variant has the nearest-half store (HN), which main (M) has built in since 13f4714a9.
#   M     main 13f4714a9 with the survey's probe patch (PROBE=/private/tmp/t0-xdck/out-m/probe-bin), no switch. Its
#         timing build also has the span dump (../scratch-span-dump-on-13f4714a9.patch).
#   X     the fix alone: light evidence of a ray cuts the history to the fast cap and leaves the box wide.
#   Xt    X with the drift test computed but not acting (TERTIUS_X_DRIFT_ACT=0): the test's own cost, for the timing.
#   D4    X with the drift test acting, rule Q3 (3 standard deviations): regional evidence cuts to the fast cap, 4 draws.
#   D8    D4, but regional evidence alone cuts to 8 draws, FullRateFrames, so its blocks keep the shared ray.
#   D4s   D4 with the ray plan's own-ray threshold FullRateFrames at 4 (TERTIUS_X_FULL_RATE=4): no own ray at 4 draws.
#   C4    X with every bounce history held at 4 draws, the control of t0-ion0.
#   Xtc, D4c, D8c   Xt, D4 and D8 with the drift test and its window ring at the blocks' center pixels only
#         (TERTIUS_X_DRIFT_CENTERS=1, the review's question: the cost of a test per block).
#   Xtn   Xt with the test's code in the shaders but run at no pixel (TERTIUS_X_DRIFT_CENTERS=2): the code's own cost.
HN=TERTIUS_X_HALF_NEAREST=1
M=""
X="TERTIUS_X_LIGHT_ACTION=cut $HN"
ring="$X TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1 TERTIUS_X_DRIFT=8 TERTIUS_X_DRIFT_RULE=1 TERTIUS_X_DRIFT_SIGMAS=3"
Xt="$ring TERTIUS_X_DRIFT_ACT=0"
D4="$ring"
D8="$ring TERTIUS_X_DRIFT_CUT=8"
D4s="$ring TERTIUS_X_FULL_RATE=4"
C4="$X TERTIUS_X_DIFFUSE_CAP=4"
Xtc="$Xt TERTIUS_X_DRIFT_CENTERS=1"
D4c="$D4 TERTIUS_X_DRIFT_CENTERS=1"
D8c="$D8 TERTIUS_X_DRIFT_CENTERS=1"
Xtn="$Xt TERTIUS_X_DRIFT_CENTERS=2"
A="TERTIUS_A_NO_CLAMP=1 $HN"
OUT_M=/private/tmp/t0-xdck/out-m/probe-bin
