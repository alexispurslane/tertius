#!/usr/bin/env bash
# Throwaway (t1-gxzg): the census runs of the probe build in a cloud job, after the build of the tree t.
# Run from the job's work folder:  bash t/tools/bf1b/job-census.sh <sill|sill-tolerance|sill-all|sheet>
# sill: part 1. The sill census of rect-lamps under ten, layer and trim, over the draws 200 to 599 of one film, with
#   one fixed exposure. sheet: part 3. The sheet census of four views under ten, layer and layer/plane, over the draws
#   16 to 47 of one film. Each run writes out/<part>-<view>-<mode>/census.txt, and census_pack.py packs the maps.
set -uo pipefail
part=$1
W=$PWD
(cd "$W/t" && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false \
  -o "$W/bin" 2>&1 | tail -2) || exit 1
run() { # <folder> <mode> <script> <from> <to> [env and demo arguments come from the caller's environment]
  local name=$1 mode=$2 script=$3
  mkdir -p "$W/out/$name"
  (cd "$W/t" && TERTIUS_BACK_FACE_PROBE=$mode TERTIUS_PROBE_CENSUS_OUT="$W/out/$name" TERTIUS_PROBE_CENSUS_FROM=$4 \
    TERTIUS_PROBE_CENSUS_TO=$5 timeout 400 "$W/bin/Tertius.Demo" --script "$script" --headless --out "$W/out/$name/run" \
    --size 1920x1080 --bloom off --video none "${@:6}" > "$W/out/$name/run.log" 2>&1)
  echo "$name exit $? $(head -1 "$W/out/$name/census.txt" 2>/dev/null)"
}
case $part in
  sill)
    export TERTIUS_PROBE_SILL_CENSUS=1 TERTIUS_PROBE_CENSUS_BOXES="glow:587:107:693:200;quiet:507:93:547:160"
    for mode in ten layer trim; do
      run "sill-rect-lamps-$mode" "$mode" tools/bf1b/census-rect-lamps.json 200 600 --exposure 2.796
    done ;;
  sill-tolerance)
    # Part 1's check of the probe's classes: ten and layer again, with a crossing tolerance of 0.3 % and of 1 %.
    export TERTIUS_PROBE_SILL_CENSUS=1 TERTIUS_PROBE_CENSUS_BOXES="glow:587:107:693:200;quiet:507:93:547:160"
    for tolerance in 3 10; do
      for mode in ten layer; do
        (export TERTIUS_PROBE_CROSSING_TOLERANCE=$tolerance
          run "sill-rect-lamps-$mode-tolerance-$tolerance" "$mode" tools/bf1b/census-rect-lamps.json 200 600 --exposure 2.796)
      done
    done
    part=sill ;;
  sill-all)
    # Part 1's check of the fade: ten and layer again, with every sample of the boxes, bright or not.
    export TERTIUS_PROBE_SILL_CENSUS=1 TERTIUS_PROBE_SILL_ALL=1 TERTIUS_PROBE_CENSUS_BOXES="glow:587:107:693:200"
    for mode in ten layer; do
      run "sill-all-rect-lamps-$mode" "$mode" tools/bf1b/census-rect-lamps.json 200 600 --exposure 2.796
    done
    exit 0 ;;
  sheet)
    export TERTIUS_PROBE_SHEET_CENSUS=1
    for view in lamp-hall-hair skin-hair hair-rugs-low demo-meshes; do
      for mode in ten layer layer/plane; do
        run "sheet-$view-${mode/\//-}" "$mode" "tools/bf1b/census-$view.json" 16 48
      done
    done ;;
esac
python3 "$W/t/tools/bf1b/census_pack.py" "$W/out" "$part"
