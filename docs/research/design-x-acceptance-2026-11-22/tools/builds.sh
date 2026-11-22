#!/usr/bin/env bash
# Tool code of t0-5tyj. builds.sh: the builds of the acceptance takes, into /private/tmp/t0-5tyj.
#   x:  the line's head with tools/renderer-survey/probe.patch, by the survey's own probe-build (step 0's patch).
#   xa: the same head with probe-acxm.patch of t0-acxm, the control of step 0: its still must equal x's byte for byte.
#   m:  main c4c1f1f2f with main's survey probe.
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd)
export BASE=/private/tmp/t0-5tyj; mkdir -p $BASE
X=${X:-$(git -C "$R" rev-parse HEAD)}
[ -e $BASE/x/probe-bin/Tertius.Demo ] || (cd "$R" && nix develop --command python3 -I tools/renderer-survey.py probe-build $BASE/x \
  && echo "$X tools/renderer-survey/probe.patch (probe-build)" > $BASE/x/build.txt)
[ -e $BASE/xa/probe-bin/Tertius.Demo ] || bash "$T/build.sh" xa "$X" "$R/docs/research/t0-acxm-region-evidence/probe-acxm.patch"
[ -e $BASE/m/probe-bin/Tertius.Demo ] || bash "$T/build.sh" m c4c1f1f2f c4c1f1f2f:tools/renderer-survey/probe.patch
