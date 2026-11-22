#!/usr/bin/env bash
# Tool code of t0-5tyj (ticket t4f-71qp). cloudvideo.sh <walk|switch> <target ms> [sweep targets...]: one cloud job's
# work, run by gpu_job.py in the job's folder, which holds the trees x (the line's head) and m (main c4c1f1f2f), inside
# the dev shell. For each side it:
#   1. builds the probe demo: the tree's own survey probe and ../scratch-dynamic-scale.patch (X also
#      ../scratch-region-log.patch, which the dynamic patch's context needs; the region log stays off);
#   2. renders the scene's survey script (renderer-survey.py: walk_script of cabin-walk, or switch_script of the switch
#      cabin with its lamps off from draw 193 to 393) as pictures: the default ACES curve and bloom, the probe's fixed
#      exposure of 4.08 stops, 1920 x 1080, under the dynamic render scale FrameTimeTarget(<target>, 0.5, 1), with each
#      draw's render size logged (out/<scene>-<side>-sizes);
#   3. encodes the video's frames into out/<scene>-<side>.mp4 and writes their region means (cloudvideo.py);
#   4. runs the survey's own take of the scene (walk cabin-walk or switch cabin) at the same target, with the size log,
#      for the settle draws (out-<side>/results).
# Then, with sweep targets, it renders the scene's script on X once per target with the size log only and no pictures
# (TERTIUS_PROBE_KEEP empty), to find a target at which the scale moves in the middle of its range.
set -uo pipefail
scene=$1; target=$2; shift 2
D=x/docs/research/design-x-acceptance-2026-11-22
mkdir -p out
python3 -I - "$scene" out/script.json <<'PY'
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
if [ "$scene" = switch ]; then
  lamps=(TERTIUS_PROBE_LAMP_SEL=z:-12:4 TERTIUS_PROBE_LAMP_OFF=193-393); part=switch; case=cabin
else
  lamps=(); part=walk; case=cabin-walk
fi
for side in m x; do
  echo "$(date +%T) build $side"
  (cd $side && patch -p1 --forward -s -i tools/renderer-survey/probe.patch \
    && { [ $side = m ] || patch -p1 --forward -s -i ../$D/scratch-region-log.patch; } \
    && patch -p1 --forward -s -i ../$D/scratch-dynamic-scale.patch \
    && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false \
       -o ../out-$side/probe-bin | tail -1)
done
for side in m x; do
  echo "$(date +%T) render $side: $(uptime)"
  env "${lamps[@]}" TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=$target TERTIUS_X_SIZE_LOG=out/$scene-$side-sizes \
    out-$side/probe-bin/Tertius.Demo --script out/script.json --headless --out frames-$side --size 1920x1080 > out/$scene-$side-run.log 2>&1
  echo "$(date +%T) render $side exit $?"
  python3 -I $D/tools/cloudvideo.py "$scene" frames-$side out/$scene-$side
  rm -rf frames-$side
  echo "$(date +%T) survey $side"
  (cd x && TERTIUS_SURVEY_HOLDS_LOCK=1 TERTIUS_X_FRAME_TIME_TARGET=$target TERTIUS_X_SIZE_LOG=../out/$scene-$side-survey-sizes \
    python3 -I tools/renderer-survey.py run ../out-$side --parts $part --only $case --no-build 2>&1 | grep -v "^  " | tail -2)
done
for t in "$@"; do
  echo "$(date +%T) sweep $t"
  env "${lamps[@]}" TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=$t TERTIUS_X_SIZE_LOG=out/sweep-$t \
    TERTIUS_PROBE_SERIES=out/sweep-$t-series.txt TERTIUS_PROBE_KEEP= \
    out-x/probe-bin/Tertius.Demo --script out/script.json --headless --out sweep-$t --size 1920x1080 > /dev/null 2>&1
  rm -rf sweep-$t out/sweep-$t-series.txt
done
ls -la out
