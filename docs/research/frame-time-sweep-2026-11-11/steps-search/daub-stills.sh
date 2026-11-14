#!/usr/bin/env bash
# daub-stills.sh <variant> [ENV=VALUE ...]: the renderer survey's still part of the three daub cases of t7-3e22 with the
# probe build parp, inside a held GPU lock. Each case gives the shown still (draw 132) and its long reference, the mean of
# 20 later captures. Results in daub/<variant>.
set -u
S=/private/tmp/tertius-sweep-20261111
W=/Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0
v=$1; shift
mkdir -p $S/daub/$v; ln -sfn $S/bin-parp $S/daub/$v/probe-bin
cd $W && env "$@" TERTIUS_SURVEY_HOLDS_LOCK=1 python3 -I tools/renderer-survey.py run $S/daub/$v --parts still \
  --only rect-lamps bay-sunbeam docking-bay --no-build --chunk 0 2>&1 | grep -v "^$" | tail -n 6
echo "daub stills $v: exit $? ($(ls $S/daub/$v/images/still 2>/dev/null | wc -l | tr -d ' ') images)"
