#!/usr/bin/env bash
# Tool code of the region repair's replay (t0-qvm1). queue.sh: every still of the replay, one run at a time (still.py):
#   part 1: the still cabin on r2-main and r2-noreg, the full picture;
#   part 2: the lamp hall and the bay on r2-main, the full picture (the region logs of the frozen stills);
#   p:      the lamp hall, the bay, exit-cabin and the cabin on r2-main with the bounce light alone (signal 2).
# A run whose region log exists is kept, so that a stopped queue resumes. A run starts only at 20 GiB free or more.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd)
for spec in r2-main:cabin:full r2-noreg:cabin:full r2-main:lamp:full r2-main:bay:full \
            r2-main:lamp:bounce:2 r2-main:bay:bounce:2 r2-main:exit:bounce:2 r2-main:cabin:bounce:2; do
  IFS=: read build view tag signal <<< "$spec"
  ls /private/tmp/t0-5tyj/r2/runs/$build-$view-$tag/region/region-*.txt > /dev/null 2>&1 && continue
  free=$(df -Pk /System/Volumes/Data | awk 'NR == 2 {print int($4 / 1048576)}')
  [ "$free" -lt 20 ] && { echo "$(date +%T) only $free GiB free: stop"; exit 2; }
  echo "$(date +%T) $build $view $tag"
  python3 -I "$T/still.py" $build $view $tag $signal || echo "$(date +%T) failed: $build $view $tag"
done
echo "$(date +%T) queue done"
