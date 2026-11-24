#!/usr/bin/env bash
# Tool code of t1-yi11. turnlook.sh <name> <yaw degrees> <pitch degrees>: rect-bay's film (bay-camera, 180 frames one tick
# apart) with build yh's fixed turn of the camera, keeping frames 0, 45, 90, 135 and 179 into
# /private/tmp/t1-yi11/look/<name>/, at rect-bay's fixed -0.359 stops (EXPOSURE=<stops> sets another), to see where the sun
# patch is in the turned view.
set -uo pipefail
name=$1; yaw=$2; pitch=$3
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd)
out=/private/tmp/t1-yi11/look/$name; command rm -rf "${out:?}"; mkdir -p $out
python3 -I - > $out/script.json <<'PY'
import json, sys
steps = [{"do": "camera", "entity": "bay-camera"}, {"do": "film", "name": "m0000", "frames": 180, "every": 1}]
json.dump({"scene": "demo-surface", "window": {"width": 1920, "height": 1080}, "steps": steps}, sys.stdout)
PY
cd "$R"
env TERTIUS_PROBE_EXPOSURE=${EXPOSURE:--0.35925741} TERTIUS_PROBE_SERIES=$out/frames.series \
  TERTIUS_PROBE_KEEP=m0000-000000,m0000-000045,m0000-000090,m0000-000135,m0000-000179 \
  TERTIUS_X_YAW_DEGREES=$yaw TERTIUS_X_PITCH_DEGREES=$pitch \
  nix develop --command bash tools/gpu-lock.sh --shared $out/lock.log /private/tmp/t1-yi11/yh/probe-bin/Tertius.Demo \
  --script $out/script.json --headless --out $out --size 1920x1080 > $out/run.log 2>&1
echo "$(date +%T) $name yaw $yaw pitch $pitch exit $? $(command ls $out/*.png 2>/dev/null | wc -l) pictures"
