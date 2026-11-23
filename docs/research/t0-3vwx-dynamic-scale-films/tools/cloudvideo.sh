#!/usr/bin/env bash
# Tool code of t0-5tyj (ticket t4f-71qp), copied from 726bf338c into t0-3vwx's record with two changes: its folder (D)
# is this record's, and a mode "times" (below, after the original's text). The original's text follows; for t0-3vwx, x is X5 and m is main ac5f23915.
# One cloud job's work, run by gpu_job.py in the job's folder, which holds the
# trees x (the line's head, with this folder unpacked into it) and m (main c4c1f1f2f), inside the dev shell. The scene is
# the walk (renderer-survey.py: walk_script of cabin-walk) or the switch (switch_script of the switch cabin, its lamps
# off from draw 193 to 393). Every run renders at 1920 x 1080 under the dynamic render scale FrameTimeTarget(<target>,
# 0.5, 1) of the scratch patch ../scratch-dynamic-scale.patch, and logs each draw's render size.
#
#   cloudvideo.sh sizes <targets...>: builds both sides. Then, with nothing else running in the job, for each scene, side
#     and target, it renders the scene's script without pictures (TERTIUS_PROBE_KEEP empty), as the survey's own takes
#     do, and keeps its size log in out/sizes/<scene>-<side>-<target>/. These runs choose the render scale of the videos.
#     Last, it plays the walk's X log at the first target back (TERTIUS_X_SIZE_REPLAY) into out/sizes/replay-check/, to
#     show that the playback gives each draw the logged size.
#   cloudvideo.sh regions <scene> <targets...>: builds X and plays its size log of each target back without pictures,
#     with the region log, into out/<scene>-x-<target>-replay/.
#   cloudvideo.sh video <scene> <side> <target> [survey]: builds the side, then renders the scene's script as pictures
#     (the default ACES curve and bloom, the probe's fixed exposure of 4.08 stops), with each draw's render scale played
#     back from sizes/<scene>-<side>-<target>/ of this folder (the sizes job's log). A picture run cannot choose its own
#     scale: its PNG writes stall the frames, and the controller then holds the scale at its floor of 0.5. The run writes
#     the PNGs of the video's frames only (TERTIUS_PROBE_KEEP, which needs TERTIUS_PROBE_SERIES) and logs its sizes into
#     out/<scene>-<side>-<target>-replay/, on X with the region log. cloudvideo.py encodes them into out/<scene>-<side>.mp4 with their region
#     means. With "survey", it then runs the survey's own take of the scene at the target, for the settle draws.
set -uo pipefail
D=x/docs/research/t0-3vwx-dynamic-scale-films
mode=$1; shift
mkdir -p out
script() {
  python3 -I - "$1" "out/$1.json" <<'PY'
import importlib.util, json, sys
from pathlib import Path
spec = importlib.util.spec_from_file_location("survey", Path("x/tools/renderer-survey.py"))
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
cases = survey.load_cases()
if sys.argv[1] == "walk":
    script = survey.walk_script(next(c for c in cases["walks"] if c["name"] == "cabin-walk"))
else:
    script = survey.switch_script(next(c for c in cases["switches"] if c["name"] == "cabin"))
Path(sys.argv[2]).write_text(json.dumps(script, indent=1))
PY
}
lamps() { if [ "$1" = switch ]; then echo TERTIUS_PROBE_LAMP_SEL=z:-12:4 TERTIUS_PROBE_LAMP_OFF=193-393; else echo TERTIUS_X_NONE=1; fi; }
build() {
  echo "$(date +%T) build $1"
  local probe=tools/renderer-survey/probe.patch; [ $1 = x ] && [ -f $D/probe-x.patch ] && probe=../$D/probe-x.patch
  (cd $1 && patch -p1 --forward -s -i $probe \
    && { [ $1 = m ] || patch -p1 --forward -s -i ../$D/scratch-region-log.patch; } \
    && patch -p1 --forward -s -i ../$D/scratch-dynamic-scale.patch \
    && { [ -z "${PASSLOG:-}" ] || patch -p1 --forward -s -i ../$D/scratch-pass-log.patch; } \
    && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false \
       -o ../out-$1/probe-bin | tail -1)
}
# run <scene> <side> <target> <size log folder> [extra environment...]: one run of the scene's script without pictures.
run() {
  local scene=$1 side=$2 target=$3 log=$4; shift 4
  env $(lamps $scene) TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=$target TERTIUS_X_SIZE_LOG=$log \
    TERTIUS_PROBE_SERIES=series-$side.txt TERTIUS_PROBE_KEEP= "$@" \
    out-$side/probe-bin/Tertius.Demo --script out/$scene.json --headless --out runs-$side --size 1920x1080 > /dev/null 2>&1
  echo "$(date +%T) $scene $side $target exit $?"
  rm -rf runs-$side series-$side.txt
}
logname() { if [ "$1" = switch ]; then echo size-193-393.txt; else echo size-none.txt; fi; }
# region <scene> <side> <target>: on X, the environment that writes the region log (scratch-region-log.patch) beside
# the played-back run's size log. The region test depends on the render sizes and the images only, not on the frame
# time, so a played-back run logs the same region test as the run that chose the sizes.
region() { [ $2 = x ] && echo TERTIUS_X_REGION_LOG=out/$1-$2-$3-replay; }
# t0-3vwx: cloudvideo.sh times <runs> <targets...>: builds both sides with scratch-pass-log.patch too, then, for each scene,
# side and target, <runs> runs without pictures under the dynamic scale, each with its size log, its time log (GPU span
# and the controller's sums per collected frame) and its per-pass log, into out/times/<scene>-<side>-<target>-r<run>/.
case $mode in
times)
  runs=$1; shift
  script walk; script switch
  PASSLOG=1 build m; PASSLOG=1 build x
  for scene in walk switch; do for side in m x; do for t in "$@"; do for ((i = 1; i <= runs; i++)); do
    o=out/times/$scene-$side-$t-r$i
    run $scene $side $t $o TERTIUS_X_TIME_LOG=$o TERTIUS_X_PASS_LOG=$o
  done; done; done; done
  gzip -9 out/times/*/pass-*.txt ;;  # a job returns at most about 6 MB of files
regions)
  scene=$1; shift
  script $scene; build x
  for t in "$@"; do
    run $scene x $t out/$scene-x-$t-replay TERTIUS_X_SIZE_REPLAY=$D/sizes/$scene-x-$t/$(logname $scene) $(region $scene x $t)
  done ;;
sizes)
  script walk; script switch
  build m; build x
  for scene in walk switch; do for side in m x; do for t in "$@"; do
    run $scene $side $t out/sizes/$scene-$side-$t
  done; done; done
  run walk x $1 out/sizes/replay-check TERTIUS_X_SIZE_REPLAY=out/sizes/walk-x-$1/size-none.txt ;;
video)
  scene=$1; side=$2; target=$3; survey=${4:-}
  script $scene; build $side
  if [ "$scene" = switch ]; then
    part=switch; case=cabin; log=size-193-393.txt
    keep=$(for k in $(seq 140 459); do printf 'f-%06d,' $k; done)
  else
    part=walk; case=cabin-walk; log=size-none.txt
    keep=$( (for k in $(seq 60 119); do printf 'g-%06d,' $k; done; for k in $(seq 0 199); do printf 'h-%06d,' $k; done) )
  fi
  echo "$(date +%T) render $scene $side: $(uptime)"
  env $(lamps $scene) TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=$target \
    TERTIUS_X_SIZE_REPLAY=$D/sizes/$scene-$side-$target/$log TERTIUS_X_SIZE_LOG=out/$scene-$side-$target-replay \
    TERTIUS_PROBE_SERIES=frames-$side.series TERTIUS_PROBE_KEEP="$keep" $(region $scene $side $target) \
    out-$side/probe-bin/Tertius.Demo --script out/$scene.json --headless --out frames-$side --size 1920x1080 > out/$scene-$side-run.log 2>&1
  echo "$(date +%T) render exit $?"
  python3 -I $D/tools/cloudvideo.py "$scene" frames-$side out/$scene-$side
  rm -rf frames-$side frames-$side.series
  if [ "$survey" = survey ]; then
    echo "$(date +%T) survey $scene $side"
    (cd x && TERTIUS_SURVEY_HOLDS_LOCK=1 TERTIUS_X_FRAME_TIME_TARGET=$target TERTIUS_X_SIZE_LOG=../out/$scene-$side-$target-survey \
      python3 -I tools/renderer-survey.py run ../out-$side --parts $part --only $case --no-build 2>&1 | grep -v "^  " | tail -2)
  fi ;;
esac
ls -la out
