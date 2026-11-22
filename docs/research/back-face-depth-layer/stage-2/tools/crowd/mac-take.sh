#!/usr/bin/env bash
# Throwaway (t1-kiaw): one Mac timing take of one scene under one exclusive hold of the GPU lock, after the method of
# stage 1b (stage-1b/tools/cost/mac-take2.sh). Run it from the worktree's root, inside the dev shell:
#   nix develop --command bash -c 'bash mac-take.sh <out> <name> <script> <stops> <first round> <rounds> <extra env> <side>...'
# Every side is a set of the probe's environment variables on the one probe binary (side_env). The take runs one
# untimed warm-up run of every side, then rounds in the rows of a Williams square of the sides: over n rounds each side
# runs once at each position and directly after each other side once. Every run has the fixed exposure <stops>, runs
# beside the GPU clock sampler, and records its clock (tools/gpu_clock.py).
set -uo pipefail
out=$1; name=$2; script=$3; stops=$4; first=$5; rounds=$6; extra=$7; shift 7
if [[ ${1-} == --inner ]]; then inner=1; shift; else inner=0; fi
sides=("$@")
n=${#sides[@]}
tree=$(pwd)
bin=/private/tmp/t1-kiaw/bin/Tertius.Demo
export GPU_CALIBRATION_BIN=/private/tmp/tertius-survey-20261109/clockbin
dir=$out/$name
mkdir -p "$dir"
side_env() {
  case $1 in
    off) echo TERTIUS_BACK_FACE_PROBE=off ;;
    pass) echo TERTIUS_BACK_FACE_PROBE=ten ;;
    cards) echo TERTIUS_BACK_FACE_PROBE=ten TERTIUS_BACK_FACE_PASS=cards ;;
    scissor|clipped|empty) echo TERTIUS_BACK_FACE_PROBE=ten TERTIUS_BACK_FACE_SPLIT=$1 ;;
    pulled|packed) echo TERTIUS_BACK_FACE_PROBE=ten TERTIUS_BACK_FACE_FETCH=$1 ;;
    *) echo "unknown side $1" >&2; return 1 ;;
  esac
}
williams() { # <n> <round>: the row of a Williams square of even order n
  local n=$1 r=$2 first=(0) i lo=1 hi=$(( $1 - 1 ))
  for ((i = 1; i < n; i++)); do
    if (( i % 2 == 1 )); then first+=($lo); ((lo++)); else first+=($hi); ((hi--)); fi
  done
  local row=()
  for i in "${first[@]}"; do row+=($(( (i + r) % n ))); done
  echo "${row[@]}"
}
one_run() { # <side> <run name> <position>
  local side=$1 run=$dir/$1/$2 start code
  rm -rf "$run"; mkdir -p "$run"
  start=$(date +%s)
  env $(side_env "$side") $extra bash "$tree/tools/gpu-clock.sh" "$bin" --script "$script" --headless --out "$run" \
    --video none --in-flight 2 --pass-timestamps off --exposure "$stops" > "$run/run.log" 2>&1
  code=$?
  python3 -I "$tree/tools/gpu_clock.py" record "$run/run.log" "$run/gpu-timings.json" > /dev/null 2>&1
  echo "$name $2 position $3 side $side exit $code seconds $(( $(date +%s) - start )) load $(uptime | sed 's/.*averages*: //')"
}
take() {
  echo "$name sides ${sides[*]} stops $stops extra '$extra'"
  for side in "${sides[@]}"; do one_run "$side" "warm-up-$first" -1; rm -rf "$dir/$side/warm-up-$first"; done
  for ((r = first; r < first + rounds; r++)); do
    local k=0
    for i in $(williams "$n" "$r"); do one_run "${sides[$i]}" "run-$r" $k; ((k++)); done
  done
}
if (( inner )); then
  take
  exit $?
fi
bash "$tree/tools/gpu-lock.sh" "$dir/take-$first.log" bash "$0" "$out" "$name" "$script" "$stops" "$first" "$rounds" "$extra" --inner "${sides[@]}"
echo "take $name $first..$((first + rounds - 1)) exit $?" >> "$out/takes.log"
