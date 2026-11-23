#!/usr/bin/env bash
# Tool code of t0-gumm. One cloud job's work for the fair test's video (card.sh with CARDRUN=cardfair.sh submits it through
# tools/runpod/gpu_job.py), run in the job's folder, which holds the tree x (X5, the line's merge tree e232080f) with this
# record's job files unpacked into x/$D, inside the dev shell. cardfair.sh <target ms> <code...>: a code is "today" (X5 with
# scratch-catch-up-share.patch and the shape 1 8 0, today's cliff), or "<label>=<peak>,<end>,<fall>[,<light>]", a shape on
# the share with the cap (scratch-catch-up-share.patch, then scratch-light-evidence-cap.patch), such as none=0,32,0 or
# h16=0.5,16,0,0. Both builds carry X5's probe patch, t0-3vwx's region, dynamic-scale and history-log patches, this record's
# own-ray log, and scratch-first-filmed.patch and scratch-frozen.patch last (the builds xgz and xcz of builds.sh).
# The film is fairrun.sh's bay start: demo-surface's bay-camera after 420 ticks, then 200 frozen captures (f-n is draw n),
# at 1920 x 1080 under the dynamic render scale FrameTimeTarget(<target>, 0.5, 1), the fixed exposure of -4.0 stops, no
# pictures kept. First one warm-up run of each build (t58-phgr, t1-80jd), whose logs are dropped. Then each code once
# (RUNS=<n>: n rounds), each logging its render size and time per draw into out/card/start-<label>[-r<round>]/.
set -uo pipefail
D=docs/research/t0-gumm-catch-up-spread; V=docs/research/t0-3vwx-dynamic-scale-films
target=$1; shift
mkdir -p out/card
python3 -I - > out/start.json <<'PY'
import json, sys
steps = [{"do": "camera", "entity": "bay-camera"}, {"do": "run", "ticks": 420}]
steps += [{"do": "capture", "name": f"f-{n:06d}"} for n in range(200)]
json.dump({"scene": "demo-surface", "window": {"width": 1920, "height": 1080}, "steps": steps}, sys.stdout, indent=1)
PY
build() {  # build <name> <patches after the own-ray log>
  cp -a x $1
  (cd $1 && patch -p1 --forward -s -i tools/renderer-survey/probe.patch \
    && for p in $V/scratch-region-log.patch $V/scratch-dynamic-scale.patch $V/scratch-history-log.patch \
                $D/scratch-ownray-log.patch $2 $D/scratch-first-filmed.patch $D/scratch-frozen.patch; do patch -p1 --forward -s -i $p || exit 1; done \
    && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false \
       -o ../out-$1/probe-bin | tail -1)
  echo "$(date +%T) build $1 exit $?"
}
build xgz $D/scratch-catch-up-share.patch
build xcz "$D/scratch-catch-up-share.patch $D/scratch-light-evidence-cap.patch"
run() {  # run <build> <log folder> [environment...]
  local b=$1 log=$2; shift 2
  mkdir -p $log
  env TERTIUS_PROBE_EXPOSURE=-4.0 TERTIUS_X_FRAME_TIME_TARGET=$target TERTIUS_X_SIZE_LOG=$log TERTIUS_X_TIME_LOG=$log \
    TERTIUS_PROBE_SERIES=series.txt TERTIUS_PROBE_KEEP= TERTIUS_X_FIRST_FILMED=1 TERTIUS_X_FROZEN=1 "$@" \
    out-$b/probe-bin/Tertius.Demo --script out/start.json --headless --out runs --size 1920x1080 > $log/run.log 2>&1
  echo "$(date +%T) start $b $* exit $? $(wc -l < $log/size-*.txt 2>/dev/null) draws"
  rm -rf runs series.txt
}
run xgz out/warm-xgz; run xcz out/warm-xcz "TERTIUS_X_CATCHUP=0 32 0"; rm -rf out/warm-*
runs=${RUNS:-1}
for ((r = 1; r <= runs; r++)); do
  sfx=; [ $runs -gt 1 ] && sfx=-r$r
  for code in "$@"; do
    if [ $code = today ]; then run xgz out/card/start-today$sfx "TERTIUS_X_CATCHUP=1 8 0"
    else label=${code%%=*}; shape=${code#*=}; run xcz out/card/start-$label$sfx "TERTIUS_X_CATCHUP=${shape//,/ }"; fi
  done
done
ls -R out/card | head -30
