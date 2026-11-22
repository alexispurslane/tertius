#!/usr/bin/env bash
# Tool code of t0-gbgj. queue-6.sh: the takes on the new sides after main moved to a819b10b2 (the walk's stored inverse
# depth). M3 = main a819b10b2, X3 = this branch's merge of it. Both builds carry their tree's survey probe patch and the
# acceptance lead's scratch-dynamic-scale.patch (a01ec40d0). In order: the size ramp on M3 (the acceptance lead's
# sizeramp.sh of b53c037f1, run by its path, 40 draws per size); after the merge commit exists, the build x3r; the
# switch "cabin" at 0.956 through the replay path on X3 (scr-X3-956, glow stays, as scr-M3-956); the size ramp on X3.
. "$(dirname "$0")/vars.sh"
T=$(cd "$(dirname "$0")" && pwd); R=$(git -C "$T" rev-parse --show-toplevel)
RAMP=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/design-x-acceptance-2026-11-22/tools/sizeramp.sh
git -C /Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144 diff --quiet b53c037f1 -- docs/research/design-x-acceptance-2026-11-22/tools/sizeramp.sh docs/research/design-x-acceptance-2026-11-22/tools/sizeramp.py || { echo "sizeramp differs from b53c037f1"; exit 1; }
ramp() { [ -e "$W/runs/ramp-$1/ramp.txt" ] && { echo "$(date +%T) kept ramp-$1"; return; }
  for try in 1 2 3; do bash "$RAMP" "$W/bins/$2/probe-bin" "$W/runs/ramp-$1" 40 && break
    echo "$(date +%T) ramp-$1 try $try failed: $(grep -m1 -o 'ErrorIncompatibleDriver' "$W/runs/ramp-$1/run.log")"; done
  echo "$(date +%T) end ramp-$1 $(grep -m1 -i 'leak' "$W/runs/ramp-$1/ramp.txt")"; }
ramp M3 m3r
until git -C "$R" log -1 --format=%s | grep -q '^Merge main a819b10b2'; do sleep 30; done
head=$(git -C "$R" rev-parse HEAD); echo "$(date +%T) X3 = $head"
[ -x "$W/bins/x3r/probe-bin/Tertius.Demo" ] || bash "$T/diskbuild.sh" "$W/logs/build-x3r.log" x3r "$head" "$head:tools/renderer-survey/probe.patch" "$W/scratch-dynamic-scale.patch"
echo "$(date +%T) build x3r exit $? $(grep -E 'before|lowest|after' "$W/logs/build-x3r.log" | tr '\n' ' ')"
TERTIUS_PROBE_LAMP_GLOW=stays TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_REPLAY=$W/replay-0956.txt bash "$T/scalefilm.sh" scr-X3-956 x3r
ramp X3 x3r
echo "$(date +%T) queue-6 done"
