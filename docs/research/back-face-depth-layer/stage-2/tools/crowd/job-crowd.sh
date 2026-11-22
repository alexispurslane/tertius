#!/usr/bin/env bash
# Throwaway (t1-kiaw): the cloud timing of the back-face pass's cost by vertex count and of its remedies, on the tree t.
# Run from the job's work folder:  bash t/tools/bf2/job-crowd.sh <part>...
# Parts: crowd, cabin, counts, fetch. One build serves every side: each side is a set of environment variables of the probe.
# A take runs one scene: one untimed warm-up run of every side (bug ticket t1-s5c1: the first run of a side with the
# pass was slow on a fresh worker), then rounds in the rows of a Williams square of the sides, so that each side runs once
# at each position and directly after each other side once per square. Every run has the scene's fixed exposure.
set -uo pipefail
W=$PWD
mkdir -p "$W/out"
(cd "$W/t" && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false \
  -o "$W/bin" 2>&1 | tail -2) || exit 1
CROWD_STOPS=-9.2693568   # stage 1b's fixed exposure of the crowd on this card (cost.md)
CABIN_STOPS=-0.4267480   # and of the cabin
side_env() { # <side>: the probe's variables of a side
  case $1 in
    off) echo TERTIUS_BACK_FACE_PROBE=off ;;
    pass) echo TERTIUS_BACK_FACE_PROBE=ten ;;
    cards) echo TERTIUS_BACK_FACE_PROBE=ten TERTIUS_BACK_FACE_PASS=cards ;;
    scissor|clipped|empty) echo TERTIUS_BACK_FACE_PROBE=ten TERTIUS_BACK_FACE_SPLIT=$1 ;;
    pulled|packed) echo TERTIUS_BACK_FACE_PROBE=ten TERTIUS_BACK_FACE_FETCH=$1 ;;
    *) echo "unknown side $1" >&2; return 1 ;;
  esac
}
williams() { # <n> <round>: the row of a Williams square of even order n, as side indices
  local n=$1 r=$2 first=(0) i lo=1 hi=$(( $1 - 1 ))
  for ((i = 1; i < n; i++)); do
    if (( i % 2 == 1 )); then first+=($lo); ((lo++)); else first+=($hi); ((hi--)); fi
  done
  local row=()
  for i in "${first[@]}"; do row+=($(( (i + r) % n ))); done
  echo "${row[@]}"
}
one_run() { # <scene folder> <side> <run name> <script> <stops> <extra env>
  local dir=$1 side=$2 name=$3 script=$4 stops=$5 extra=$6 start code
  mkdir -p "$dir/$side/$name"
  start=$(date +%s)
  (cd "$W/t" && env $(side_env "$side") $extra timeout 300 "$W/bin/Tertius.Demo" --script "$script" --headless \
    --out "$dir/$side/$name" --size 1920x1080 --video none --exposure "$stops" > "$dir/$side/$name.log" 2>&1)
  code=$?
  echo "$side $name exit $code seconds $(( $(date +%s) - start )) $(grep -h 'GPU frame span' "$dir/$side/$name.log" | head -1)" \
    | tee -a "$dir/take.log"
}
take() { # <scene name> <script> <stops> <rounds> <extra env> <side>...
  local name=$1 script=$2 stops=$3 rounds=$4 extra=$5; shift 5
  local sides=("$@") n=$# dir="$W/out/$name"
  mkdir -p "$dir"
  nvidia-smi --query-gpu=timestamp,clocks.sm,clocks.mem,temperature.gpu,power.draw --format=csv -lms 500 > "$dir/clocks.csv" 2>&1 &
  local clocks=$!
  echo "take $name sides ${sides[*]} rounds $rounds stops $stops extra '$extra'" | tee -a "$dir/take.log"
  for side in "${sides[@]}"; do one_run "$dir" "$side" warm-up "$script" "$stops" "$extra"; done
  for ((r = 1; r <= rounds; r++)); do
    local row; row=$(williams "$n" "$(( r - 1 ))")
    for i in $row; do one_run "$dir" "${sides[$i]}" "run-$r" "$script" "$stops" "$extra"; done
  done
  kill "$clocks" 2> /dev/null
  rm -rf "$dir"/*/warm-up
  find "$dir" -name '*.png' -delete
}
for part in "$@"; do
  case $part in
    crowd) take crowd scripts/ter-kh4g-crowd-timing.json $CROWD_STOPS 8 "" off pass cards scissor clipped empty pulled packed ;;
    cabin) take cabin scripts/ter-kh4g-cabin-timing.json $CABIN_STOPS 6 "" off pass cards scissor clipped empty ;;
    counts)
      for count in 1 10 25 50; do
        take "crowd-$count" scripts/ter-kh4g-crowd-timing.json $CROWD_STOPS 4 "TERTIUS_CROWD_COUNT=$count" off pass
      done ;;
    *) echo "unknown part $part" >&2 ;;
  esac
done
echo done | tee -a "$W/out/summary.txt"
