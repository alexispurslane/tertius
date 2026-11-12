# Sourced by the take scripts. One take of the GPU lock holds several traces; each trace takes an inner lock of its own.
S=/private/tmp/t2-mlvy
export GPU_CALIBRATION_BIN=/private/tmp/tertius-survey-20261109/clockbin
export TERTIUS_GPU_LOCK=/tmp/t2-mlvy-inner.lock TERTIUS_GPU_QUEUE=/tmp/t2-mlvy-inner.queue
watchdog() { # Ends an xctrace that hangs after its target exits: three minutes after the line, INT, then TERM.
  local log=$1 name=$2
  until grep -q 'uptime-start' $log 2>/dev/null; do sleep 5; done
  until grep -q 'Target app exited' $log 2>/dev/null; do sleep 5; grep -q 'uptime-end' $log && return; done
  sleep 180
  pkill -INT -f "xctrace record.*$name/run.trace"; sleep 30; pkill -TERM -f "xctrace record.*$name/run.trace"
}
trace() { # trace <name> <bin> <script> [demo options]
  local name=$1 bin=$2 script=$3; shift 3
  [ -f $S/trace/$name/passes.json ] && return
  rm -rf $S/trace/$name; mkdir -p $S/trace
  ( sleep 10; watchdog $S/trace/$name/run.log $name ) &
  local dog=$!
  TRACE_DEMO_BIN=$S/bin-$bin/Tertius.Demo bash $S/gpu-trace-bin.sh --no-counters --start 1 --frames 120 --out $S/trace/$name \
    $script "$@" > $S/trace/$name.log 2>&1
  kill $dog 2>/dev/null
  echo "trace $name: $(grep -m1 'frame span' $S/trace/$name/passes.md) | $(grep -m1 'direct light' $S/trace/$name/passes.md) | $(grep -m1 '^| transparent' $S/trace/$name/passes.md) | $(grep -m1 'GPU clock over' $S/trace/$name/passes.md | cut -c1-60)"
}
