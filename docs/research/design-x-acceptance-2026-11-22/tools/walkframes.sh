#!/usr/bin/env bash
# Tool code of t0-5tyj. walkframes.sh <name> <probe build folder>: the survey's walk cabin-walk once more on one side,
# with the survey's script (walk_script) and the probe's fixed exposure of 4.08 stops, but as the game shows it: the
# default ACES curve and bloom, at 1920 x 1080. (The survey's walk runs with --tone-curve hable, under which the probe
# writes each capture as 24-bit log luminance, a code and no picture.) It keeps every capture's PNG: the walk's 120
# draws (film "g") and the 200 draws after the stop (film "h"), one film frame per draw. Output: $BASE/walkframes/<name>.
# The survey keeps no frame of a walk, so walkvideo.py needs this run for the video of the stop.
set -uo pipefail
name=$1; bin=$2
R=$(cd "$(dirname "$0")/../../../.." && pwd)
out=${BASE:-/private/tmp/t0-5tyj}/walkframes/$name
[ -f "$out/h-000199.png" ] && { echo "$(date +%T) kept walkframes $name"; exit 0; }
rm -rf "$out"; mkdir -p "$out"
cd "$R"
nix develop --command python3 -I - "$out/script.json" <<'PY'
import importlib.util, json, sys
from pathlib import Path
spec = importlib.util.spec_from_file_location("survey", Path.cwd() / "tools" / "renderer-survey.py")
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
case = next(c for c in survey.load_cases()["walks"] if c["name"] == "cabin-walk")
Path(sys.argv[1]).write_text(json.dumps(survey.walk_script(case), indent=1))
PY
echo "$(date +%T) start walkframes $name"
TERTIUS_PROBE_EXPOSURE=4.08 nix develop --command bash tools/gpu-lock.sh --shared "$out/lock.log" "$bin/Tertius.Demo" --script "$out/script.json" \
  --headless --out "$out" --size 1920x1080 > "$out/run.log" 2>&1
echo "$(date +%T) end walkframes $name exit $? $(ls "$out"/h-*.png 2>/dev/null | wc -l) frames"
