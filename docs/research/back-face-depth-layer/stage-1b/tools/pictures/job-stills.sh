#!/usr/bin/env bash
# Throwaway (t1-gxzg): the stills of one view in a cloud job, after the build of the tree t (and of main for the control).
# Run from the job's work folder:  bash t/tools/bf1b/job-stills.sh <walls|hair> <script in t> [main]
# The set walls renders the columns ten, layer and layerfade (layer+fade), the set hair ten, layer and plane
# (layer/plane). First one run of ten with the automatic exposure gives the stops of the film's frame 239, and every column
# then runs with them. Each column keeps the frame 239 of its film, and ten also the frame 231, the noise floor's partner.
# With "main", the tree main runs too, with the same stops: the control against ten. The frames go to out/pack as
# lossless WebP, and out/summary.txt holds the stops and the control.
set -uo pipefail
set=$1; script=$2; control=${3:-}
W=$PWD
mkdir -p "$W/out/pack"
build() {
  (cd "$W/$1" && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false \
    -o "$W/bin-$1" 2>&1 | tail -2)
}
run() { # <column> <tree> <mode> [demo arguments]
  local name=$1 tree=$2 mode=$3; shift 3
  mkdir -p "$W/out/$name"
  (cd "$W/t" && TERTIUS_BACK_FACE_PROBE=$mode timeout 400 "$W/bin-$tree/Tertius.Demo" --script "$script" --headless \
    --out "$W/out/$name" --size 1920x1080 --bloom off "$@" > "$W/out/$name/run.log" 2>&1)
  echo "$name exit $?" | tee -a "$W/out/summary.txt"
  find "$W/out/$name" -name 's-*.png' ! -name s-000239.png ! -name s-000231.png -delete
}
build t || exit 1
if [[ $control == main ]]; then build main || exit 1; fi
run auto t ten --video none
stops=$(python3 -c "import json,sys; print(next(e['stops'] for e in json.load(open(sys.argv[1])) if e['capture'] == 's-000239'))" "$W/out/auto/exposure.json")
echo "stops $stops" | tee -a "$W/out/summary.txt"
case $set in
  walls) columns="ten:ten layer:layer layerfade:layer+fade" ;;
  hair) columns="ten:ten layer:layer plane:layer/plane" ;;
esac
for c in $columns; do run "${c%%:*}" t "${c#*:}" --exposure "$stops"; done
if [[ $control == main ]]; then
  run main main ten --exposure "$stops"
  cmp "$W/out/main/s-000239.png" "$W/out/ten/s-000239.png" && echo "control: main and ten equal at frame 239" | tee -a "$W/out/summary.txt"
  cmp "$W/out/main/s-000231.png" "$W/out/ten/s-000231.png" && echo "control: main and ten equal at frame 231" | tee -a "$W/out/summary.txt"
fi
python3 - "$W/out" <<'PY'
import sys
from pathlib import Path
from PIL import Image
out = Path(sys.argv[1])
for png in sorted(out.glob("*/s-0002*.png")):
    if png.parent.name == "main":
        continue
    Image.open(png).convert("RGB").save(out / "pack" / f"{png.parent.name}-{png.stem[2:]}.webp", lossless=True, method=4, quality=100)
print("pack", sum(p.stat().st_size for p in (out / "pack").iterdir()))
PY
