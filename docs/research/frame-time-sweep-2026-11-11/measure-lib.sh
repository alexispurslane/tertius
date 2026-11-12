# Sourced by the take scripts. One take of the GPU lock holds several traces; each trace takes an inner lock of its own.
S=/private/tmp/tertius-sweep-20261111
W=/Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0
export GPU_CALIBRATION_BIN=/private/tmp/tertius-survey-20261109/clockbin
export TERTIUS_GPU_LOCK=/tmp/tertius-sweep-inner.lock TERTIUS_GPU_QUEUE=/tmp/tertius-sweep-inner.queue
TAKE_START=$(date +%s)
TAKE_LIMIT=${TAKE_LIMIT:-1080}  # seconds: start no new job after 18 minutes
in_time() { [ $(( $(date +%s) - TAKE_START )) -lt $TAKE_LIMIT ]; }
watchdog() { # Ends an xctrace that hangs after its target exits: three minutes after the line, INT, then TERM.
  local log=$1 name=$2
  until grep -q 'uptime-start' $log 2>/dev/null; do sleep 5; done
  until grep -q 'Target app exited' $log 2>/dev/null; do sleep 5; grep -q 'uptime-end' $log && return; done
  sleep 180
  pkill -INT -f "xctrace record.*$name/run.trace"; sleep 30; pkill -TERM -f "xctrace record.*$name/run.trace"
}
trace() { # trace <name> <bin> <script> [trace options and demo options]; TRACE_COUNTERS=1 records counters
  local name=$1 bin=$2 script=$3; shift 3
  [ -f $S/trace/$name/run/gpu-timings.json ] && return
  in_time || { echo "skip $name: the take's time is over"; return; }
  rm -rf $S/trace/$name; mkdir -p $S/trace
  ( sleep 10; watchdog $S/trace/$name/run.log $name ) &
  local dog=$!
  local counters=(--no-counters); [ "${TRACE_COUNTERS:-0}" = 1 ] && counters=()
  ( cd $W && TRACE_RECORD_ONLY=1 TRACE_DEMO_BIN=$S/bin-$bin/Tertius.Demo bash $S/gpu-trace-bin.sh "${counters[@]}" --start ${TRACE_START:-1} --frames ${TRACE_FRAMES:-120} --out $S/trace/$name \
    $script "$@" ) > $S/trace/$name.log 2>&1
  kill $dog 2>/dev/null
  echo "trace $name: $(grep -m1 'GPU frame span' $S/trace/$name/run.log) | $(grep -m1 'GPU clock over' $S/trace/$name/run.log | cut -c1-70)"
}
report() { # report <name> [report options]: the passes table of a recorded trace, outside the lock
  local name=$1; shift
  [ -f $S/trace/$name/passes.md ] && return
  ( cd $W && python3 -I tools/gpu-trace.py report $S/trace/$name/run.trace $S/trace/$name/run/gpu-timings.json $S/trace/$name --log $S/trace/$name/run.log "$@" && python3 -I $S/tools/pays.py $S/trace/$name/export $S/trace/$name/run/gpu-timings.json 9 --json $S/trace/$name/pays.json > $S/trace/$name/pays.txt && rm -rf $S/trace/$name/export $S/trace/$name/run.trace ) > $S/trace/$name.report.log 2>&1
  echo "report $name: $(grep -m1 'frame span' $S/trace/$name/passes.md)"
}
