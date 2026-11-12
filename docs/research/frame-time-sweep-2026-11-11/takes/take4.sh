#!/usr/bin/env bash
# Take 4 (timed, exclusive): the traces that did not fit into take 3. It reads take4.jobs when
# it starts, so the list can grow while the take waits in the queue. Run in the dev shell, under the GPU lock.
set -u
. /private/tmp/tertius-sweep-20261111/measure-lib.sh
D=scripts/temporal-upscale-timing.json
cp $S/take4.jobs $S/take4.jobs.ran
. $S/take4.jobs.ran
echo "take 4 ended after $(( $(date +%s) - TAKE_START )) s"
