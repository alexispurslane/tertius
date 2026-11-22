#!/usr/bin/env bash
# Tool code of t0-gbgj. builds.sh <x2 rev>: the four builds of the takes, by build.sh, into /private/tmp/t0-gbgj/bins.
#   m2: main 284025851 with scratch-probe-m2.patch.  x2: the merged head with its own tools/renderer-survey/probe.patch.
#   m2/plain and x2/plain: the same revisions without the probe, for the timing rounds (round.sh).
T=$(cd "$(dirname "$0")" && pwd); . "$T/vars.sh"
X2_REV=${1:?usage: builds.sh <x2 rev>}
bash "$T/build.sh" m2 "$MAIN_REV" "$T/../scratch-probe-m2.patch"
bash "$T/build.sh" x2 "$X2_REV" "$X2_REV:tools/renderer-survey/probe.patch"
PLAIN=1 bash "$T/build.sh" m2 "$MAIN_REV"
PLAIN=1 bash "$T/build.sh" x2 "$X2_REV"
