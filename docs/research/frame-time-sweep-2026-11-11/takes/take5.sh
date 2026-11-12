#!/usr/bin/env bash
# Take 5 (timed, exclusive): the step caps. It reads take5.jobs when
# it starts, so the list can grow while the take waits in the queue. Run in the dev shell, under the GPU lock.
set -u
. /private/tmp/tertius-sweep-20261111/measure-lib.sh
D=scripts/temporal-upscale-timing.json
cp $S/take5.jobs $S/take5.jobs.ran
. $S/take5.jobs.ran
echo "take 5 ended after $(( $(date +%s) - TAKE_START )) s"
