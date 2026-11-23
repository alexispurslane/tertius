#!/usr/bin/env bash
# Tool code of t58-3n6v (the cold start check). One job on a fresh card, in the job's folder, which holds the tree main
# (2e8fdaed2), inside the dev shell. It builds main's Release demo with main's tools/renderer-survey/probe.patch, the
# ter-bcwr record's scratch-takes-build.patch (size, time, first-filmed) and pass-log.patch (each pass's GPU time per
# draw). Then the first GPU run of the job is the cold run: no warm-up before it. Then a warm run of the same script.
# Script cold.json: the cabin's person camera filmed from draw 0 (TERTIUS_X_FIRST_FILMED=1) for 150 frames, then three
# times: one frame on the chase camera and 150 frames on the person camera. Logs go to out/card/<cold|warm>/.
set -uo pipefail
mkdir -p out/card
gpu() { nvidia-smi --query-gpu=name,pstate,clocks.sm,clocks.mem,temperature.gpu,persistence_mode --format=csv,noheader 2>&1; }
echo "$(date +%T) before build: $(gpu)" > out/card/env.txt
ls -la ~/.nv ~/.cache/nvidia 2>&1 | head -20 >> out/card/env.txt
cp -a main build-main
(cd build-main && patch -p1 --forward -s -i tools/renderer-survey/probe.patch \
  && for p in ../job/*.patch; do patch -p1 --forward -s -i $p || exit 1; done \
  && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false \
     -o ../out-main/probe-bin | tail -1)
echo "$(date +%T) build exit $?" >> out/card/env.txt
for run in cold warm; do
  log=out/card/$run; mkdir -p $log
  echo "$(date +%T) before $run: $(gpu)" >> out/card/env.txt
  SECONDS=0
  TERTIUS_X_FIRST_FILMED=1 TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_LOG=$log \
    TERTIUS_X_TIME_LOG=$log TERTIUS_X_PASS_LOG=$log TERTIUS_PROBE_SERIES=series.txt TERTIUS_PROBE_KEEP= \
    out-main/probe-bin/Tertius.Demo --script job/cold.json --headless --out runs --size 1920x1080 > $log/run.txt 2>&1
  echo "$(date +%T) $run exit $? $(wc -l < $log/size-none.txt) draws, ${SECONDS} s" >> out/card/env.txt
  echo "$(date +%T) after $run: $(gpu)" >> out/card/env.txt
  rm -rf runs series.txt
  ls ~/.nv ~/.cache/nvidia 2>&1 | head -5 >> out/card/env.txt
done
cat out/card/env.txt
