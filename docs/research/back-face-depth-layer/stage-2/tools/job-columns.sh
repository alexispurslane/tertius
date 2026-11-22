#!/usr/bin/env bash
# Throwaway (t1-g4p1): the columns of one view in a cloud job, after the build of the tree t, at a fixed exposure.
# Run from the job's work folder:  bash t/tools/bf2/job-columns.sh <script in t> <stops> <name>:<mode> ...
# Each column runs the probe's mode <mode> (TERTIUS_BACK_FACE_PROBE) with --exposure <stops>, --size 1920x1080 and
# --bloom off, and keeps the frame 239 of its film; the column ten also keeps the frame 231, the noise floor's partner.
# The frames go to out/pack as lossless WebP, and out/summary.txt holds the exit codes.
set -uo pipefail
script=$1; stops=$2; shift 2
W=$PWD
mkdir -p "$W/out/pack"
(cd "$W/t" && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false \
  -o "$W/bin" 2>&1 | tail -2) || exit 1
for c in "$@"; do
  name=${c%%:*}; mode=${c#*:}
  mkdir -p "$W/out/$name"
  (cd "$W/t" && TERTIUS_BACK_FACE_PROBE=$mode timeout 400 "$W/bin/Tertius.Demo" --script "$script" --headless \
    --out "$W/out/$name" --size 1920x1080 --bloom off --exposure "$stops" > "$W/out/$name/run.log" 2>&1)
  echo "$name $mode exit $?" | tee -a "$W/out/summary.txt"
  find "$W/out/$name" -name 's-*.png' ! -name s-000239.png ! -name s-000231.png -delete
done
python3 - "$W/out" <<'PY'
import sys
from pathlib import Path
from PIL import Image
out = Path(sys.argv[1])
for png in sorted(out.glob("*/s-0002*.png")):
    if png.stem == "s-000231" and png.parent.name != "ten":
        continue
    Image.open(png).convert("RGB").save(out / "pack" / f"{png.parent.name}-{png.stem[2:]}.webp", lossless=True, method=4, quality=100)
print("pack", sum(p.stat().st_size for p in (out / "pack").iterdir()))
PY
