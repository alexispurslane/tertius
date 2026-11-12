#!/usr/bin/env bash
# Take 6 (timed, exclusive): the acceptance trace of the patched MoltenVK of the flake (ticket ter-2zga). It reads
# take6.jobs when it starts. Run in the dev shell of the ticket's branch, under the GPU lock.
set -u
. /private/tmp/tertius-sweep-20261111/measure-lib.sh
D=scripts/temporal-upscale-timing.json
cp $S/take6.jobs $S/take6.jobs.ran
. $S/take6.jobs.ran
echo "take 6 ended after $(( $(date +%s) - TAKE_START )) s"
