#!/usr/bin/env bash
# Tool code (tools/README.md, "The GPU clock of a timed run"): one timed headless run of the Release demo, under the GPU
# lock through tools/gpu-lock.sh and beside gpu-clock through tools/gpu-clock.sh. Run it inside the dev shell:
#   nix develop --command bash tools/demo-timing.sh <out folder> <script.json> [demo options]
#   nix develop --command bash tools/demo-timing.sh captures/gpu-branch scripts/demo-exit.json --size 1920x1080
# Run it from the checkout whose demo it times. It builds the demo in Release and runs the built binary with --headless
# --out <out folder> --video none and the demo options, so the run holds no build. The run's output goes to
# <out folder>/run.log. The script writes the clock over the run's timed span into <out folder>/gpu-timings.json as
# "gpuClock", and prints the report's warning and the GPU lines of the run: valid pass times, the frame span, requested
# range sums and the clock. tools/gpu_clock.py compare compares two such runs. A demo from before ter-qk78 prints no
# markers, so its clock is unknown.
set -uo pipefail

usage="usage: tools/demo-timing.sh <out folder> <script.json> [demo options]"
out=$(realpath -m "${1:?$usage}")
script=$(realpath "${2:?$usage}")
shift 2
# The tools come from this script's checkout, and the demo from the checkout of the working directory, so that a
# checkout of an older base, which lacks these tools, runs its own demo with them.
tools=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
cd "$(git rev-parse --show-toplevel)"
mkdir -p "$out"

dotnet build src/Tertius.Demo -c Release -v quiet -nologo > "$out/build.log" 2>&1 || { tail -20 "$out/build.log" >&2; exit 1; }
bash "$tools/gpu-lock.sh" "$out/run.log" bash "$tools/gpu-clock.sh" src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo \
    --script "$script" --headless --out "$out" --video none "$@"
code=$?
if [[ "$code" -ne 0 ]]; then
    echo "The demo run failed (exit code $code). The end of $out/run.log:" >&2
    tail -20 "$out/run.log" >&2
    exit "$code"
fi
if [[ ! -f "$out/gpu-timings.json" ]]; then
    echo "The run wrote no gpu-timings.json: its renderer times no pass, or the script shows no frame. See $out/run.log." >&2
    exit 1
fi
python3 -I "$tools/gpu_clock.py" record "$out/run.log" "$out/gpu-timings.json" > /dev/null
python3 -I - "$out/gpu-timings.json" <<'PY'
import json
import sys

with open(sys.argv[1]) as timings:
    warning = json.load(timings).get("warning")
if warning:
    print(warning)
PY
grep -E '^GPU ' "$out/run.log"
