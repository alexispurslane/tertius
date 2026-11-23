#!/usr/bin/env bash
# Tool code of ter-bcwr. One cloud job's work on the RTX 2000 Ada (card-submit.sh submits it through main's
# tools/runpod/gpu_job.py), run in the job's folder, which holds the trees today (c776bed47, one render scale) and build
# (the commit of ter-bcwr, one render scale per cost kind), inside the dev shell. card.sh <target ms> <runs> [event...]:
# builds each tree's Release demo with its own tools/renderer-survey/probe.patch and then the patches of job/<code>/ in
# their names' order: for today the acceptance record's scratch-dynamic-scale-step2.patch and t0-gumm's
# scratch-first-filmed.patch, for build this record's scratch-takes-build.patch, the same two fitted to the build. Then one
# warm-up run of each build, whose logs are dropped (t58-phgr, t1-80jd). Then <runs> rounds, each running both codes in
# turn, the order swapped each round, without pictures, at 1920 x 1080 under the dynamic render scale
# FrameTimeTarget(<target>, 0.5, 1), with the render size and the GPU span of every draw logged into
# out/card/<event>-<code>-r<round>/:
#   start: t0-gumm's start.json, the cabin's person camera filmed from draw 0 for 200 frames (TERTIUS_X_FIRST_FILMED=1);
#   cut: t0-gumm's cut.json, a still and 100 film frames on the person camera, one frame on the chase camera (draw 133),
#     then the person camera again from draw 134 for 200 frames: the cut back restarts every history.
# The events default to start and cut. The warm-up runs the first event.
set -uo pipefail
target=$1; runs=$2; shift 2; events=${*:-start cut}
mkdir -p out/card
cp job/*.json out/
for code in today build; do
  cp -a $code build-$code
  (cd build-$code && patch -p1 --forward -s -i tools/renderer-survey/probe.patch \
    && for p in ../job/$code/*.patch; do patch -p1 --forward -s -i $p || exit 1; done \
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
warm=${events%% *}
run today $warm out/warm-today; run build $warm out/warm-build; rm -rf out/warm-*
for ((r = 1; r <= runs; r++)); do
  order="today build"; [ $((r % 2)) = 0 ] && order="build today"
  for event in $events; do
    for code in $order; do run $code $event out/card/$event-$code-r$r; done
  done
done
ls -R out/card | head -40
