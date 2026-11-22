#!/usr/bin/env bash
# Tool code of t0-9fo0, part B. builds-b.sh: the four builds of part B, by build.sh, into /private/tmp/t0-9fo0b.
#   x:  the line's head 6d8a68332 with the remedy's probe (probe-acxm.patch of t0-acxm) and the first form of the region
#       log (scratch-region-log-v1.patch: per draw, the counts of tested and firing anchors). Every X take ran on it. The
#       log does nothing without TERTIUS_X_REGION_LOG, and s-X2 (x2 without the variable) equals s-X byte for byte.
#   m:  main d8559eb58 with main's survey probe. x0: 631cc61cc with that tree's survey probe.
#   x2: x with the second form of the log (scratch-region-log.patch: also the ring's whole layer per draw, in a .f32 file).
T=$(cd "$(dirname "$0")" && pwd); D=$(cd "$T/.." && pwd); ACXM=$(cd "$D/../t0-acxm-region-evidence" && pwd)/probe-acxm.patch
export BASE=/private/tmp/t0-9fo0b
bash "$T/build.sh" x 6d8a68332 "$ACXM" "$D/scratch-region-log-v1.patch"
bash "$T/build.sh" m d8559eb58 d8559eb58:tools/renderer-survey/probe.patch
bash "$T/build.sh" x0 631cc61cc 631cc61cc:tools/renderer-survey/probe.patch
bash "$T/build.sh" x2 6d8a68332 "$ACXM" "$D/scratch-region-log.patch"
