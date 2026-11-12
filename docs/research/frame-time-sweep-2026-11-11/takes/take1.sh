#!/usr/bin/env bash
# Take 1 (timed, exclusive): the deck's traces of main and of the ablation variants. Run in the dev shell, under the GPU lock.
set -u
. /private/tmp/tertius-sweep-20261111/measure-lib.sh
D=scripts/temporal-upscale-timing.json
cp $S/take1.jobs $S/take1.jobs.ran
. $S/take1.jobs.ran
echo "take 1 ended after $(( $(date +%s) - TAKE_START )) s"
