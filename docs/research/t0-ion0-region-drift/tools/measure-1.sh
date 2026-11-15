#!/usr/bin/env bash
# measure-1.sh: writes tables-step1.txt, the count of step 1 (queue 1) and the control of the second build (queue 1c).
M=$(dirname "$0")/m.sh; out=$(cd "$(dirname "$0")/.." && pwd)/tables-step1.txt
{
  echo "# Step 1: the drift test counted without action (queue-1.sh). The fix with the nearest-half store. Names k<jitter>-<case>:"
  echo "# t today's jitter, n none, l the long Halton sequence. Stills: run draws 100 to 300, every 7th. The 'shader' columns of"
  echo "# queue 1 are the first build's rule (any pixel of the block). kt2-rect-lamps is queue 1c on the second build (center pixel)."
  echo; echo "## Stills"
  $M driftcount.py still kt-rect-lamps kn-rect-lamps kl-rect-lamps kt-bay-sunbeam kn-bay-sunbeam kl-bay-sunbeam kt-exit-cabin kn-exit-cabin kl-exit-cabin kt2-rect-lamps
  echo; echo "## Cabin switch, today's jitter"
  $M driftcount.py switch kt-cabin
  echo; echo "## Film rect-bay, today's jitter"
  $M driftcount.py film kt-rect-bay
} > "$out" 2>&1
