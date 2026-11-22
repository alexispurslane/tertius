#!/usr/bin/env bash
# Throwaway (t1-yy2f): the census and stills runs of stage 2 in a cloud job, after the build of the tree t.
# Run from the job's work folder:  bash t/tools/bf1b/job-cards.sh <census|rules|stills>
# rules: the sheet rules' runs of census alone.
# census: the sheet census of three views over the draws 16 to 47 of one film, under the 10 % rule and the layer with
#   the pass of stage 1b (TERTIUS_BACK_FACE_PASS=1b) and with the pass of Design 1 (cards), and the sheet rules'
#   records under the 10 % rule with the pass of Design 1. Each run writes out/<view>-<column>/census.txt.
# stills: the view cards-glow in three columns at one exposure, the stops of the film's frame 239 under ten: ten,
#   layer with the pass of stage 1b, and layer with the pass of Design 1. Also the lamp hall's layer column with the pass
#   of Design 1, at the stops of stage 1b (5.4094524). The frames go to out/pack as lossless WebP.
set -uo pipefail
part=$1
W=$PWD
mkdir -p "$W/out/pack"
(cd "$W/t" && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false \
  -o "$W/bin" 2>&1 | tail -2) || exit 1
census() { # <folder> <pass> <mode> <script> [rules]
  local name=$1 pass=$2 mode=$3 script=$4 rules=${5:-0}
  mkdir -p "$W/out/$name"
  (cd "$W/t" && TERTIUS_BACK_FACE_PASS=$pass TERTIUS_BACK_FACE_PROBE=$mode TERTIUS_PROBE_SHEET_CENSUS=1 \
    TERTIUS_PROBE_SHEET_RULES=$rules TERTIUS_PROBE_CENSUS_OUT="$W/out/$name" TERTIUS_PROBE_CENSUS_FROM=16 \
    TERTIUS_PROBE_CENSUS_TO=48 timeout 400 "$W/bin/Tertius.Demo" --script "$script" --headless --out "$W/out/$name/run" \
    --size 1920x1080 --bloom off --video none > "$W/out/$name/run.log" 2>&1)
  echo "$name exit $? $(head -1 "$W/out/$name/census.txt" 2>/dev/null)" | tee -a "$W/out/summary.txt"
}
still() { # <column> <pass> <mode> <script> <stops or auto>
  local name=$1 pass=$2 mode=$3 script=$4 stops=$5 exposure=()
  [[ $stops != auto ]] && exposure=(--exposure "$stops")
  mkdir -p "$W/out/$name"
  (cd "$W/t" && TERTIUS_BACK_FACE_PASS=$pass TERTIUS_BACK_FACE_PROBE=$mode timeout 400 "$W/bin/Tertius.Demo" \
    --script "$script" --headless --out "$W/out/$name" --size 1920x1080 --bloom off "${exposure[@]}" > "$W/out/$name/run.log" 2>&1)
  echo "$name exit $?" | tee -a "$W/out/summary.txt"
  find "$W/out/$name" -name 's-*.png' ! -name s-000239.png ! -name s-000231.png -delete
}
case $part in
  rules)
    # The sheet rules' records alone, after the fourth rule (the plane's bounds over the pixel's square) joined.
    for view in lamp-hall-hair cards-glow skin-hair; do
      census "$view-rules" cards ten "tools/bf1b/census-$view.json" 1
    done
    python3 "$W/t/tools/bf1b/sheet_rules.py" "$W"/out/*-rules > "$W/out/sheet-rules.txt" 2>&1
    echo "sheet_rules exit $?" | tee -a "$W/out/summary.txt"
    # The control of run to run: this tree's census of the lamp hall under layer with the pass of stage 1b, twice. The
    # first stills job compared it with the census tree of stage 1b: the maps were equal, and one sample moved class.
    for run in 1 2; do
      census "control-$run" 1b layer tools/bf1b/census-lamp-hall-hair.json
    done
    cmp "$W/out/control-1/census.txt" "$W/out/control-2/census.txt" && echo "control: census equal" | tee -a "$W/out/summary.txt"
    for f in "$W"/out/control-1/*.f32; do
      cmp -s "$f" "$W/out/control-2/$(basename "$f")" && echo "control: $(basename "$f") equal" | tee -a "$W/out/summary.txt"
    done
    rm -f "$W"/out/control-*/*.f32 ;;
  census)
    for view in lamp-hall-hair cards-glow skin-hair; do
      script=tools/bf1b/census-$view.json
      [[ $view != skin-hair ]] && census "$view-ten" 1b ten "$script"
      [[ $view != skin-hair ]] && census "$view-layer-1b" 1b layer "$script"
      census "$view-layer-cards" cards layer "$script"
      census "$view-rules" cards ten "$script" 1
    done
    python3 "$W/t/tools/bf1b/sheet_rules.py" "$W"/out/*-rules > "$W/out/sheet-rules.txt" 2>&1
    echo "sheet_rules exit $?" | tee -a "$W/out/summary.txt" ;;
  stills)
    # The stops of the frame 239 under ten come from the first stills job (6e81457d), which ran out of time: -11.056582.
    stops=-11.056582
    echo "stops $stops" | tee -a "$W/out/summary.txt"
    still ten 1b ten tools/bf1b/stills-cards-glow.json "$stops"
    still layer1b 1b layer tools/bf1b/stills-cards-glow.json "$stops"
    still layercards cards layer tools/bf1b/stills-cards-glow.json "$stops"
    still hall-layercards cards layer tools/bf1b/stills-lamp-hall-hair.json 5.4094524
    python3 - "$W/out" <<'PY'
import sys
from pathlib import Path
from PIL import Image
out = Path(sys.argv[1])
for png in sorted(out.glob("*/s-0002*.png")):
    # The frame 231 only of ten, the noise floor's partner: the job returns at most about 6 MB.
    if png.parent.name == "auto" or (png.stem == "s-000231" and png.parent.name != "ten"):
        continue
    Image.open(png).convert("RGB").save(out / "pack" / f"{png.parent.name}-{png.stem[2:]}.webp", lossless=True, method=4, quality=100)
print("pack", sum(p.stat().st_size for p in (out / "pack").iterdir()))
PY
    ;;
esac
