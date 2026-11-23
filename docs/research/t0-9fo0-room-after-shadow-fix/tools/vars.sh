# Tool code of t0-9fo0. vars.sh: the sides and switches, sourced by the queues.
#   M     main d8559eb58 with the survey's probe patch (PROBE=$OUT_M), with the sun's shadow fix.
#   X0    the line's head 631cc61cc with the survey's probe patch (take.sh's default probe): no evidence from the region.
#   A     clamps off (TERTIUS_A_NO_CLAMP=1), the reference that explains a fault, on X0's build. AM: the same switch
#         on M's build, whose probe has no such switch, so AM is a second plain run of M.
#   E     the fixed exposure 4.08 stops of the earlier records' light measure.
#   NJ    no jitter (TERTIUS_DEBUG_JITTER=none, src/Tertius.Rendering/DebugJitter.cs).
OUT_M=/private/tmp/t0-9fo0/out-m/probe-bin
A="TERTIUS_A_NO_CLAMP=1"
E="TERTIUS_PROBE_EXPOSURE=4.08"
NJ="TERTIUS_DEBUG_JITTER=none"
