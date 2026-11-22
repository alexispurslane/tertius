# Tool code of t0-gbgj. vars.sh: the takes' folder and the sides, sourced by the other tools.
#   M2   main 284025851 (the back-face layer), with main's survey probe fitted to the layer (scratch-probe-m2.patch:
#        main's own probe.patch did not apply there, bug t58-k95j,
#        which main fixed in 2ffd7e66f, a tool fix only).
#   X2   the merged head (Design X's line 795e1661f merged with main 284025851), with this branch's
#        tools/renderer-survey/probe.patch (0e441286d's patch fitted to the layer).
#   A2   X2 with both history clamps off (TERTIUS_A_NO_CLAMP=1): the reference of the daub, not the bar.
#   E    the fixed exposure of 4.08 stops.
W=/private/tmp/t0-gbgj
MAIN_REV=284025851
A="TERTIUS_A_NO_CLAMP=1"
E="TERTIUS_PROBE_EXPOSURE=4.08"
