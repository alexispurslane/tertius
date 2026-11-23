#!/usr/bin/env bash
# Tool code of t0-rs55. One cloud job's work on the RTX 2000 Ada (cardwide-submit.sh submits it through main's
# tools/runpod/gpu_job.py), run in the job's folder, which holds the trees main (main 86ae0fbc8, today's fix) and wide (the
# wide fix's commit), inside the dev shell. cardwide.sh <target ms> <runs>: builds each tree's Release demo with its own
# tools/renderer-survey/probe.patch, the acceptance record's scratch-dynamic-scale-step2.patch (TERTIUS_X_FRAME_TIME_TARGET,
# TERTIUS_X_SIZE_LOG, TERTIUS_X_TIME_LOG) and t0-gumm's scratch-first-filmed.patch, all read from the tree main. Then one
# warm-up run of each build, whose logs are dropped (t58-phgr: the first run of a binary on a worker costs 230 to 239 ms at
# draw 0; t1-80jd: the first run of a job may draw 10 to 18 % slower per draw). Then <runs> rounds, each running both codes
# in turn, without pictures, at 1920 x 1080 under the dynamic render scale FrameTimeTarget(<target>, 0.5, 1), with the
# render size and the GPU span of every draw logged into out/card/<event>-<code>-r<round>/:
#   start: t0-gumm's start.json, the cabin's person camera filmed from draw 0 for 200 frames (TERTIUS_X_FIRST_FILMED=1, for
#     the start only: the first frame draws once, so draw k is film frame k);
#   cut: t0-gumm's cut.json, the cabin's person camera, a still and 100 film frames, one frame on the chase camera (draw 133),
#     then the person camera again from draw 134 for 200 frames: the cut back restarts every history.
set -uo pipefail
D=docs/research/t0-gumm-catch-up-spread; A=docs/research/design-x-acceptance-2026-11-22
target=$1; runs=$2
mkdir -p out/card
cp main/$D/start.json out/start.json; cp main/$D/cut.json out/cut.json
for code in main wide; do
  cp -a $code build-$code
  (cd build-$code && patch -p1 --forward -s -i tools/renderer-survey/probe.patch \
    && for p in ../main/$A/scratch-dynamic-scale-step2.patch ../main/$D/scratch-first-filmed.patch; do
         patch -p1 --forward -s -i $p || exit 1; done \
    && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false \
       -o ../out-$code/probe-bin | tail -1)
  echo "$(date +%T) build $code exit $?"
done
run() {  # run <code> <event> <log folder>
  local code=$1 event=$2 log=$3
  mkdir -p $log
  local first=(TERTIUS_X_NONE=1); [ $event = start ] && first=(TERTIUS_X_FIRST_FILMED=1)
  env "${first[@]}" TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=$target TERTIUS_X_SIZE_LOG=$log \
    TERTIUS_X_TIME_LOG=$log TERTIUS_PROBE_SERIES=series.txt TERTIUS_PROBE_KEEP= \
    out-$code/probe-bin/Tertius.Demo --script out/$event.json --headless --out runs --size 1920x1080 > $log/run.log 2>&1
  echo "$(date +%T) $event $code exit $? $(wc -l < $log/size-*.txt 2>/dev/null) draws"
  rm -rf runs series.txt
}
run main start out/warm-main; run wide start out/warm-wide; rm -rf out/warm-*
for ((r = 1; r <= runs; r++)); do
  order="main wide"; [ $((r % 2)) = 0 ] && order="wide main"
  for event in start cut; do
    for code in $order; do run $code $event out/card/$event-$code-r$r; done
  done
done
ls -R out/card | head -40
