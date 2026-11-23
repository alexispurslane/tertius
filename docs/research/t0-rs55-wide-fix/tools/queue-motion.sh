#!/usr/bin/env bash
# Tool code of t0-rs55. queue-motion.sh: the settled stills and the films of motion of the wide fix's takes, today (m0z, main
# 86ae0fbc8) against the wide fix (m1z), with builds.sh's builds.
#   1. The survey (tools/renderer-survey.py run --no-build, at the budget's configuration) of the stills rect-lamps,
#      bay-sunbeam, docking-bay and exit-cabin and of the walk cabin-walk, into /private/tmp/t0-gbgj/rs-survey-<build>
#      (its probe-bin is a link to the build's). Each still keeps its shown picture, images/still/<name>.png, which the
#      queue compares byte for byte between the builds; the walk's result holds each region's draws to settle after the stop.
#   2. The turn: orbit's person camera after 60 ticks (the walk's view), a still, 60 film frames standing (f), then 60 film
#      frames (g000 to g059) each after a mouse move of 10 px to the left (PersonControl.TurnPerPixel: 0.025 rad per draw,
#      86 degrees in all), then 180 frames standing (h), at 1920 x 1080 and the headless render scale 2/3, the fixed exposure
#      of 4.08 stops. Into /private/tmp/t0-gbgj/rs-turn-<build>: frames.series (the region means of every frame), and the
#      PNGs of g000, g030, g059 and h 0, 1, 2, 4, 8 and 16.
# A run starts at 20 GiB of free disk or more (the line orchestrator's limit); the queue stops under 20.
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd); B=/private/tmp/t0-gbgj
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
stop() { [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }; }
for build in m0z m1z; do
  out=$B/rs-survey-$build; mkdir -p $out; ln -sfn $B/bins/$build/probe-bin $out/probe-bin
  stop
  (cd "$R" && nix develop --command python3 -I tools/renderer-survey.py run $out --no-build --parts still,walk \
    --only rect-lamps bay-sunbeam docking-bay exit-cabin cabin-walk > $out/survey.log 2>&1)
  echo "$(date +%T) survey $build exit $?: $(ls $out/results/still 2>/dev/null | wc -l) stills, $(ls $out/results/walk 2>/dev/null | wc -l) walks"
done
for name in rect-lamps bay-sunbeam docking-bay exit-cabin; do
  a=$B/rs-survey-m0z/images/still/$name.png; b=$B/rs-survey-m1z/images/still/$name.png
  if cmp -s $a $b; then echo "still $name: the same bytes"; else echo "still $name: DIFFERENT"; fi
done
for build in m0z m1z; do
  out=$B/rs-turn-$build; rm -rf $out; mkdir -p $out
  (cd "$R" && python3 -I - $out/script.json <<'PY'
import importlib.util, json, sys
from pathlib import Path
spec = importlib.util.spec_from_file_location("survey", Path("tools/renderer-survey.py"))
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
turn = []
for k in range(60):
    turn += [{"do": "post", "to": {"kind": "topic", "name": "input"}, "event": "InputEvent",
              "payload": {"kind": "mouseMove", "delta": [10, 0]}},
             {"do": "film", "name": f"g{k:03d}", "frames": 1, "every": 1}]
steps = [{"do": "capture", "name": "still"}, {"do": "film", "name": "f", "frames": 60, "every": 1}, *turn,
         {"do": "film", "name": "h", "frames": 180, "every": 1}]
Path(sys.argv[1]).write_text(json.dumps(survey.scene_script("orbit", "person", 60, steps), indent=1))
PY
  )
  keep=g000-000000,g030-000000,g059-000000,h-000000,h-000001,h-000002,h-000004,h-000008,h-000016
  stop
  (cd "$R" && env TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_PROBE_SERIES=$out/frames.series TERTIUS_PROBE_KEEP=$keep \
    nix develop --command bash tools/gpu-lock.sh --shared $out/lock.log $B/bins/$build/probe-bin/Tertius.Demo \
    --script $out/script.json --headless --out $out --size 1920x1080 > $out/run.log 2>&1)
  echo "$(date +%T) turn $build exit $?: $(wc -l < $out/frames.series 2>/dev/null) series rows, $(ls $out/*.png 2>/dev/null | wc -l) pictures"
done
echo "$(date +%T) queue-motion.sh done"
