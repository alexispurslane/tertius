#!/usr/bin/env bash
# Tool code (t2b-xeo7). film.sh <bin> <scene> <out>: a film of 840 draws, one tick each (14 s at 60 a second), of the
# scratch scene <scene> (film-probe.patch: lit-block-back-walk or lit-block-back-turn) at 1280 x 720, the default headless
# render scale 2/3 and a fixed 6.79 stops (the lit back's exposure of the research take), under the shared GPU lock. The
# probe logs the environment map's faces per draw into <out>/faces.txt (TERTIUS_X_FACE_LOG).
set -u
bin=$1; scene=$2; out=$3
R=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
/bin/rm -rf "$out"; mkdir -p "$out"
cat > "$out/script.json" <<JSON
{ "scene": "$scene", "window": { "width": 1280, "height": 720 },
  "steps": [ { "do": "film", "name": "f", "frames": 840, "every": 1 } ] }
JSON
for try in 1 2 3; do
  /bin/rm -f "$out/faces.txt"
  TERTIUS_X_FACE_LOG="$out/faces.txt" nix develop "$R" --command bash "${GPU_LOCK:-/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh}" \
    --shared "$out/lock.log" "$bin" --script "$out/script.json" --headless --out "$out/frames" --exposure 6.79 \
    > "$out/run.log" 2>&1 && break
  grep -q ErrorIncompatibleDriver "$out/run.log" || break
done
echo "$scene: $(ls "$out/frames" | grep -c '^f-') frames, $(wc -l < "$out/faces.txt") face lines"
