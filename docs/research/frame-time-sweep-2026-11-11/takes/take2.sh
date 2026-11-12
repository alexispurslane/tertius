#!/usr/bin/env bash
# Take 2 (timed, exclusive): the deck's traces of main and of the ablation variants that take 1 did not reach. Run in the dev shell, under the GPU lock.
set -u
. /private/tmp/tertius-sweep-20261111/measure-lib.sh
D=scripts/temporal-upscale-timing.json
cp $S/take1.jobs $S/take2.jobs.ran
trace deck-maind-t2 maind $D   # this take's own baseline
. $S/take2.jobs.ran
echo "take 2 ended after $(( $(date +%s) - TAKE_START )) s"
