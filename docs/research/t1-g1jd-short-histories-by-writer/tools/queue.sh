#!/usr/bin/env bash
# Tool code of t1-g1jd. queue.sh: every still of the count, one run at a time: the views lamp, cabin and bay (still.py), on
# the builds g1-main, g1-noreg, g1-noal and g1-none (builds.sh), twice each (r1, r2), so that the two runs of a build show
# whether a still repeats bit for bit. A run whose dump exists is kept, so that a stopped queue resumes. Each run holds the
# GPU lock in its shared mode for about 25 s. A run starts only at 20 GiB free or more.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd)
for view in lamp cabin bay; do
  for build in g1-main g1-noreg g1-noal g1-none; do
    for run in r1 r2; do
      ls /private/tmp/t0-5tyj/g1jd/runs/$build-$view-$run/dump/*.f32 > /dev/null 2>&1 && continue
      free=$(df -Pk /System/Volumes/Data | awk 'NR == 2 {print int($4 / 1048576)}')
      [ "$free" -lt 20 ] && { echo "$(date +%T) only $free GiB free: stop"; exit 2; }
      echo "$(date +%T) $build $view $run"
      python3 -I "$T/still.py" $build $view $run || echo "$(date +%T) failed: $build $view $run"
    done
  done
done
echo "$(date +%T) queue done"
