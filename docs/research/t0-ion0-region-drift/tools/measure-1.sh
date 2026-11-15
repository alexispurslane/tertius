#!/usr/bin/env bash
# measure-1.sh: writes tables-step1.txt, the count of step 1 on the third build (queue 1m), which holds Q0, Q1, Q3 and
# Q2, and the controls of the second and third builds (queues 1c and 1m).
M=$(dirname "$0")/m.sh; out=$(cd "$(dirname "$0")/.." && pwd)/tables-step1.txt
{
  echo "# Step 1: the drift test counted without action (queue-1m.sh, the third build). The fix with the nearest-half store."
  echo "# Names m<jitter>-<case>: t today's jitter, n none, l the long Halton sequence. Stills: run draws 100 to 300, every 7th."
  echo "# Controls: kt-rect-lamps is queue 1 on the first build (its 'shader' columns are the rule 'any pixel of the block')."
  echo "# kt2-rect-lamps is queue 1c on the second build (center pixel, no Q2). mt-rect-lamps must repeat its numbers."
  echo; echo "## Controls"
  $M driftcount.py still kt-rect-lamps kt2-rect-lamps
  echo; echo "## Stills"
  $M driftcount.py still mt-rect-lamps mn-rect-lamps ml-rect-lamps mt-bay-sunbeam mn-bay-sunbeam ml-bay-sunbeam mt-exit-cabin mn-exit-cabin ml-exit-cabin
  echo; echo "## Cabin switch, today's jitter"
  $M driftcount.py switch mt-cabin
  echo; echo "## Film rect-bay, today's jitter"
  $M driftcount.py film mt-rect-bay
  echo; echo "## The wait before the first cut, today's jitter"
  $M driftcount.py wait mt-cabin mt-rect-bay:film
} 2>&1 | grep -v -i "warning\|sqlite" > "$out"
