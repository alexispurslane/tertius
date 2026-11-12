#!/usr/bin/env bash
# Take 3 (timed, exclusive): the traces that the full disk cut from take 2, and the new variants. It reads take3.jobs when
# it starts, so the list can grow while the take waits in the queue. Run in the dev shell, under the GPU lock.
set -u
. /private/tmp/tertius-sweep-20261111/measure-lib.sh
D=scripts/temporal-upscale-timing.json
cp $S/take3.jobs $S/take3.jobs.ran
. $S/take3.jobs.ran
echo "take 3 ended after $(( $(date +%s) - TAKE_START )) s"
