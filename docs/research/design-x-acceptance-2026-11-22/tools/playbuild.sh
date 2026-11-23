#!/usr/bin/env bash
# Tool code of t0-5tyj. playbuild.sh <m|x|xb|xc|xd|mfix>: builds the probe demo that macdyn.sh runs, into $BASE/<side>play/probe-bin
# (default BASE /private/tmp/t0-5tyj), from a scratch copy of the side's tree outside the checkout, which it deletes
# after the build:
#   x: the checkout's HEAD (the line's src), its probe patch tools/renderer-survey/probe.patch from the working tree (with
#      the lamp switch of TERTIUS_PROBE_LAMP_GLOW), then ../scratch-region-log.patch and ../scratch-dynamic-scale.patch;
#   xb: the x tree with ../scratch-light-evidence-cap.patch (experiment XB: light evidence cuts a history to at most
#      FullRateFrames draws, as region evidence does, in place of the fast cap);
#   xc: the x tree with ../scratch-plan-rate-at-fast-cap.patch (experiment XC: the ray plan gives no ray of its own to a
#      history of the fast cap's 4 draws or more);
#   xd: the x tree with ../scratch-plan-waits-for-evidence.patch (experiment XD: the ray plan gives no ray of its own
#      while light evidence holds the block's histories at the fast cap);
#   m: main c4c1f1f2f, main's probe patch with the same lamp switch (../probe-main-2ffd7e66f-glow.patch, written against
#      main 2ffd7e66f, whose probe patch differs from c4c1f1f2f's in two lines; ShaderConstants.cs takes c4c1f1f2f's own
#      hunks, see below), then ../scratch-dynamic-scale.patch;
#   mfix: the m tree with ../scratch-render-scale-t0-gbgj.patch (a copy of t0-gbgj's patch: TERTIUS_X_RENDER_SCALE gives
#      a headless run the constant FixedRenderScale, with the render targets sized for it) in place of the dynamic patch.
# PATCHSET=step2 takes the scratch patches fitted to main's step 2 of the wobble fix (../scratch-*-step2.patch), and
# OUT=<name> names the build's folder $BASE/<name>play in place of the side's.
set -euo pipefail
side=$1
R=$(cd "$(dirname "$0")/../../../.." && pwd)
D=$R/docs/research/design-x-acceptance-2026-11-22
B=${BASE:-/private/tmp/t0-5tyj}
free=$(df -BG / | awk 'NR == 2 { gsub("G", "", $4); print $4 }')
# The line's disk limit for a rebuild in a probe folder (2026-11-22): start at 22 GiB free or more.
[ "$free" -ge 22 ] || { echo "only $free GiB free, no build"; exit 1; }
tree=$B/${side}play-src; rm -rf $tree; mkdir -p $tree
if [ $side = x ] || [ $side = xb ] || [ $side = xc ] || [ $side = xd ]; then
  git -C $R archive HEAD | tar -x -C $tree
  patch -d $tree -p1 --forward -s -i $R/tools/renderer-survey/probe.patch
  patch -d $tree -p1 --forward -s -i $D/scratch-region-log${PATCHSET:+-$PATCHSET}.patch
  case $side in
  xb) patch -d $tree -p1 --forward -s -i $D/scratch-light-evidence-cap.patch ;;
  xc) patch -d $tree -p1 --forward -s -i $D/scratch-plan-rate-at-fast-cap.patch ;;
  xd) patch -d $tree -p1 --forward -s -i $D/scratch-plan-waits-for-evidence.patch ;;
  esac
else
  git -C $R archive c4c1f1f2f | tar -x -C $tree
  # Main's glow patch fails its hunk of ShaderConstants.cs on c4c1f1f2f. That hunk is not part of the lamp switch, so
  # that file's hunks come from c4c1f1f2f's own probe patch.
  python3 -I $D/tools/patchfiles.py $D/probe-main-2ffd7e66f-glow.patch drop src/Tertius.Rendering/ShaderConstants.cs \
    | patch -d $tree -p1 --forward -s
  git -C $R show c4c1f1f2f:tools/renderer-survey/probe.patch \
    | python3 -I $D/tools/patchfiles.py /dev/stdin keep src/Tertius.Rendering/ShaderConstants.cs | patch -d $tree -p1 --forward -s
fi
if [ $side = mfix ]; then
  patch -d $tree -p1 --forward -s -i $D/scratch-render-scale-t0-gbgj.patch
else
  patch -d $tree -p1 --forward -s -i $D/scratch-dynamic-scale${PATCHSET:+-$PATCHSET}.patch
fi
cd $R && nix develop --command bash -c "cd $tree && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false \
  -p:TreatWarningsAsErrors=false -o $B/${OUT:-$side}play/probe-bin 2>&1 | tail -3"
rm -rf $tree
echo "$(date +%T) built $B/${OUT:-$side}play/probe-bin"
