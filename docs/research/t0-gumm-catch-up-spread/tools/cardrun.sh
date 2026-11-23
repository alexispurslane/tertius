#!/usr/bin/env bash
# Tool code of t0-gumm. One cloud job's work (card.sh submits it through tools/runpod/gpu_job.py), run in the job's folder,
# which holds the tree x (X5, the line's merge tree e232080f) with this record's job files unpacked into x/$D, inside the
# dev shell. cardrun.sh <target ms> <code...>: a code is "cap" (X5 with the acceptance record's
# scratch-light-evidence-cap.patch, today's cliff in the plan), a catch-up shape "<peak>,<end>,<fall>[,<light>]" on X5
# with scratch-catch-up-share.patch ("1,8,0" is today's code; light 0 turns the two light clocks off), or
# "cap+<shape>", the shape with the cap (the share's patch, then the cap's). The builds carry X5's probe patch and t0-3vwx's region,
# dynamic-scale and history-log patches, and this record's own-ray log; the logs that wait for the device are off here.
# First one warm-up run of each build (the first run of a binary on a worker costs 230 to 239 ms at draw 0, t58-phgr),
# whose logs are dropped. The warm-up also covers t1-80jd: the first run of a job may draw 10 to 18 % slower per draw. Then for each code two runs without pictures at 1920 x 1080 under the dynamic render scale
# FrameTimeTarget(<target>, 0.5, 1), each logging its render size and its time per draw (TERTIUS_X_SIZE_LOG,
# TERTIUS_X_TIME_LOG) into out/card/<event>-<code>/:
#   switch: the survey's switch "cabin" (switch_script), the fair lamp switch (the glow goes off with the light), lamps
#     off at draw 193 and on at 393; its draws 0 to 60 are also the start of a run;
#   cut: cut.json, the cabin's person camera (the switch's view) for a still and 100 film frames, one frame on the chase
#     camera, then the person camera again from draw 134 for 200 frames: the second cut restarts every history.
set -uo pipefail
D=docs/research/t0-gumm-catch-up-spread; V=docs/research/t0-3vwx-dynamic-scale-films
target=$1; shift
mkdir -p out/card
python3 -I - out/switch.json <<'PY'
import importlib.util, json, sys
from pathlib import Path
spec = importlib.util.spec_from_file_location("survey", Path("x/tools/renderer-survey.py"))
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
case = next(c for c in survey.load_cases()["switches"] if c["name"] == "cabin")
Path(sys.argv[1]).write_text(json.dumps(survey.switch_script(case), indent=1))
PY
cp x/$D/cut.json out/cut.json
build() {  # build <name> <last patch>
  cp -a x $1
  (cd $1 && patch -p1 --forward -s -i tools/renderer-survey/probe.patch \
    && for p in $V/scratch-region-log.patch $V/scratch-dynamic-scale.patch $V/scratch-history-log.patch \
                $D/scratch-ownray-log.patch $2; do patch -p1 --forward -s -i $p || exit 1; done \
    && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false \
       -o ../out-$1/probe-bin | tail -1)
  echo "$(date +%T) build $1 exit $?"
}
build xg $D/scratch-catch-up-share.patch
build xb $D/scratch-light-evidence-cap.patch
build xc "$D/scratch-catch-up-share.patch $D/scratch-light-evidence-cap.patch"
run() {  # run <build> <event> <log folder> [environment...]
  local b=$1 event=$2 log=$3; shift 3
  local lamps=(TERTIUS_X_NONE=1); [ $event = switch ] && lamps=(TERTIUS_PROBE_LAMP_SEL=z:-12:4 TERTIUS_PROBE_LAMP_OFF=193-393)
  mkdir -p $log
  env "${lamps[@]}" TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=$target TERTIUS_X_SIZE_LOG=$log \
    TERTIUS_X_TIME_LOG=$log TERTIUS_PROBE_SERIES=series.txt TERTIUS_PROBE_KEEP= "$@" \
    out-$b/probe-bin/Tertius.Demo --script out/$event.json --headless --out runs --size 1920x1080 > $log/run.log 2>&1
  echo "$(date +%T) $event $b $* exit $? $(wc -l < $log/size-*.txt 2>/dev/null) draws"
  rm -rf runs series.txt
}
run xg switch out/warm-xg; run xb switch out/warm-xb; run xc switch out/warm-xc; rm -rf out/warm-*
# RUNS=<n> (default 1) runs every code n times, in rounds (each code once, then each code again), into
# out/card/<event>-<code>-r<round> when n > 1.
runs=${RUNS:-1}
for ((r = 1; r <= runs; r++)); do
  sfx=; [ $runs -gt 1 ] && sfx=-r$r
  for code in "$@"; do
    for event in switch cut; do
      if [ $code = cap ]; then run xb $event out/card/$event-cap$sfx
      elif [[ $code == cap+* ]]; then shape=${code#cap+}; run xc $event out/card/$event-$code$sfx "TERTIUS_X_CATCHUP=${shape//,/ }"
      else run xg $event out/card/$event-$code$sfx "TERTIUS_X_CATCHUP=${code//,/ }"; fi
    done
  done
done
ls -R out/card | head -50
