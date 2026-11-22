#!/usr/bin/env bash
# Throwaway (t1-r9vj, from t1-g4p1's tools/mac-columns.sh): the columns of one view on the Mac, under the shared GPU lock.
# Run inside the dev shell:
#   [NOSUN=0] [STOPS=<stops>] [COLUMNS="<name>:<mode> ..."] [MAIN=<binary folder of main> MAINTREE=<its tree>] bash mac-columns.sh <binary folder> <tree> <script> <out>
# Changes from t1-g4p1's script: every column keeps its frame 231 beside 239, for the frame noise of each column. With
# MAIN, the column main runs that binary (main with the scene's patch and no probe) at the same stops. A failed run
# runs again, up to three times.
# NOSUN=1, the default, sets TERTIUS_NO_SUN=1: the control without the sun. Without STOPS, a run of ten with the automatic
# exposure gives the stops of the film's frame 239. Every column then runs at those stops with --size 1920x1080 and
# --bloom off, and keeps the frames 239 (and 231 for ten) as lossless WebP in <out>/pack.
set -uo pipefail
bin=$1; tree=$2; script=$3; out=$4
lock=/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh
mkdir -p "$out/pack"
run() { # <name> <mode> [demo arguments]
  local name=$1 mode=$2 exe=${EXE:-$bin}; shift 2
  mkdir -p "$out/$name"
  local code try
  for try in 1 2 3; do # a run that cannot start Vulkan (ErrorIncompatibleDriver beside another line's timing) runs again
    (cd "$tree" && TERTIUS_NO_SUN=${NOSUN:-1} TERTIUS_BACK_FACE_PROBE=$mode bash "$lock" --shared "$out/$name/run.log" "$exe/Tertius.Demo" \
      --script "$script" --headless --out "$out/$name" --size 1920x1080 --bloom off "$@" > /dev/null 2>&1)
    code=$?
    [[ $code == 0 ]] && break
    sleep 30
  done
  echo "$name $mode exit $code (tries $try)" | tee -a "$out/summary.txt"
  find "$out/$name" -name 's-*.png' ! -name s-000239.png ! -name s-000231.png -delete
}
if [[ -n ${STOPS:-} ]]; then
  stops=$STOPS
else
  run auto ten
  stops=$(python3 -c "import json,sys; print(next(e['stops'] for e in json.load(open(sys.argv[1])) if e['capture'] == 's-000239'))" "$out/auto/exposure.json")
fi
echo "stops $stops" | tee -a "$out/summary.txt"
for c in ${COLUMNS:-ten:ten layer:layer layerfade:layer+fade dfront:layer+known/front/0.5 dmiss05:layer+known/miss/0.5 \
  dmiss15:layer+known/miss/1.5 dblack05:layer+known/black/0.5 dblack15:layer+known/black/1.5}; do
  run "${c%%:*}" "${c#*:}" --exposure "$stops"
done
if [[ -n ${MAIN:-} ]]; then tree=$MAINTREE EXE=$MAIN run main none --exposure "$stops"; fi
python3 - "$out" <<'PY'
import sys
from pathlib import Path
from PIL import Image
out = Path(sys.argv[1])
for png in sorted(out.glob("*/s-0002*.png")):
    if png.parent.name == "auto":
        continue
    Image.open(png).convert("RGB").save(out / "pack" / f"{png.parent.name}-{png.stem[2:]}.webp", lossless=True, method=4, quality=100)
    png.unlink()
PY
