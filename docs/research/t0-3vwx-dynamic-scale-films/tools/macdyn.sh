#!/usr/bin/env bash
# Tool code of t0-5tyj (ticket t4f-71qp), copied from 726bf338c into t0-3vwx's record with these changes: the side is
# the name of a build in /private/tmp/t0-gbgj/bins (x5h, m5h, x3h, m3h: builds.sh), the region log goes on with the X
# builds (x*), the outputs go to /private/tmp/t0-gbgj/macdyn5, and the scripts come from X5's survey (the tree
# e232080f), as the card's size logs do (cloudvideo.sh). The original's text follows.
# The Mac's runs of the walk (renderer-survey.py: walk_script of cabin-walk) or
# the switch (switch_script of the switch cabin, its lamps off from draw 193 to 393) under the scratch patch
# ../scratch-dynamic-scale.patch, at 1920 x 1080, with the probe's fixed exposure of 4.08 stops. Output: $BASE/macdyn
# (default BASE /private/tmp/t0-5tyj). The same method as the cloud's (cloudvideo.sh):
#   macdyn.sh sizes <walk|switch> <m|x> <target ms>: one run without pictures under the dynamic render scale
#     FrameTimeTarget(<target>, 0.5, 1), which holds the GPU lock alone, so that the scale follows this Mac's own frame
#     time. Its size log and its time log (each frame's GPU span and the controller's cost sums) go to
#     sizes/<scene>-<side>-<target>/.
#   macdyn.sh pictures <walk|switch> <m|x> <size log | constant scale> <name> [VAR=value...]: one picture run (the default
#     ACES curve and bloom) whose render scale each draw takes from the size log (TERTIUS_X_SIZE_REPLAY), or the same
#     scale on every draw when the argument is a number. It writes the PNGs of the film frames in KEEP (a comma-separated
#     list such as "f-000150,f-000220"; empty keeps none) into pics/<name>/, with its own size log, and on X the region
#     log. The extra VAR=value words go into its environment, such as TERTIUS_A_NO_CLAMP=1. A picture run's PNG writes do
#     not change its sizes, so it holds the lock in its shared mode.
#   macdyn.sh fixed <walk|switch> <side> <scale> <name> [VAR=value...]: as pictures, but through the fixed path: the
#     headless FixedRenderScale(<scale>) of t0-gbgj's patch (TERTIUS_X_RENDER_SCALE), whose render targets are sized for
#     that scale. The side here names the build, such as mfix.
# The binary is $BASE/<side>play/probe-bin (playbuild.sh).
set -uo pipefail
mode=$1; scene=$2; side=$3; shift 3
R=$(cd "$(dirname "$0")/../../../.." && pwd)
B=/private/tmp/t0-gbgj; O=$B/macdyn5; mkdir -p $O
if [ "$scene" = switch ]; then lamps=(TERTIUS_PROBE_LAMP_SEL=z:-12:4 TERTIUS_PROBE_LAMP_OFF=193-393); draws=493
else lamps=(TERTIUS_X_NONE=1); draws=533; fi
# t0-3vwx: the scripts come from the survey of SURVEY_TREE (default X5's tree e232080f), in $O/survey-<tree>
# (renderer-survey.py and renderer-survey/), and the scene's script is $O/<scene>-<tree>.json. For X5's tree these are
# the folder and file names of takes A and B, $O/survey and $O/<scene>.json.
ST=${SURVEY_TREE:-e232080fd2c29b94bd639d094cabe336bf7f6291}
if [ $ST = e232080fd2c29b94bd639d094cabe336bf7f6291 ]; then SV=$O/survey; SJ=$O/$scene.json; else SV=$O/survey-${ST:0:9}; SJ=$O/$scene-${ST:0:9}.json; fi
[ -d $SV/tools ] || { mkdir -p $SV && git -C "$R" archive $ST tools/renderer-survey.py tools/renderer-survey | tar -x -C $SV; }
[ -f $SJ ] || (cd "$SV" && nix develop "$R" --command python3 -I - "$scene" "$SJ" <<'PY'
import importlib.util, json, sys
from pathlib import Path
spec = importlib.util.spec_from_file_location("survey", Path.cwd() / "tools" / "renderer-survey.py")
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
cases = survey.load_cases()
if sys.argv[1] == "walk":
    script = survey.walk_script(next(c for c in cases["walks"] if c["name"] == "cabin-walk"))
else:
    script = survey.switch_script(next(c for c in cases["switches"] if c["name"] == "cabin"))
Path(sys.argv[2]).write_text(json.dumps(script, indent=1))
PY
)
cd "$R"
case $mode in
sizes)
  target=$1; out=$O/sizes/$scene-$side-$target; rm -rf $out $O/tmp-$side; mkdir -p $out
  echo "$(date +%T) sizes $scene $side $target: $(uptime)"
  env "${lamps[@]}" TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=$target TERTIUS_X_SIZE_LOG=$out TERTIUS_X_TIME_LOG=$out \
    TERTIUS_PROBE_SERIES=$O/tmp-$side.series TERTIUS_PROBE_KEEP= \
    nix develop --command bash tools/gpu-lock.sh $out/lock.log $B/bins/$side/probe-bin/Tertius.Demo --script $SJ \
    --headless --out $O/tmp-$side --size 1920x1080 > $out/run.log 2>&1
  echo "$(date +%T) exit $? $(wc -l < $out/size-*.txt) draws: $(uptime)"
  rm -rf $O/tmp-$side $O/tmp-$side.series ;;
pictures)
  replay=$1; name=$2; shift 2; out=$O/pics/$name; rm -rf $out; mkdir -p $out
  if [[ $replay =~ ^[0-9.]+$ ]]; then
    for ((k = 0; k < draws; k++)); do echo "$k 0 0 $replay"; done > $out/replay.txt; replay=$out/replay.txt
  fi
  region=(); [[ $side = x* ]] && region=(TERTIUS_X_REGION_LOG=$out)
  echo "$(date +%T) pictures $name"
  env "${lamps[@]}" TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_REPLAY=$replay \
    TERTIUS_X_SIZE_LOG=$out "${region[@]}" TERTIUS_PROBE_SERIES=$out/frames.series TERTIUS_PROBE_KEEP="${KEEP:-}" "$@" \
    nix develop --command bash tools/gpu-lock.sh --shared $out/lock.log $B/bins/$side/probe-bin/Tertius.Demo \
    --script $SJ --headless --out $out --size 1920x1080 > $out/run.log 2>&1
  echo "$(date +%T) exit $? $(ls $out/*.png 2>/dev/null | wc -l) pictures" ;;
fixed)
  scale=$1; name=$2; shift 2; out=$O/pics/$name; rm -rf $out; mkdir -p $out
  echo "$(date +%T) fixed $name"
  env "${lamps[@]}" TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_RENDER_SCALE=$scale TERTIUS_PROBE_SERIES=$out/frames.series \
    TERTIUS_PROBE_KEEP="${KEEP:-}" "$@" \
    nix develop --command bash tools/gpu-lock.sh --shared $out/lock.log $B/bins/$side/probe-bin/Tertius.Demo \
    --script $SJ --headless --out $out --size 1920x1080 > $out/run.log 2>&1
  echo "$(date +%T) exit $? $(ls $out/*.png 2>/dev/null | wc -l) pictures" ;;
esac
