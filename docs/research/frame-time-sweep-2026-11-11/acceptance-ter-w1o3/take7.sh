#!/usr/bin/env bash
# A timed take (exclusive). It reads the job list that its first argument names, so that two queued takes never share
# one list file. Run in the dev shell, under the GPU lock.
set -u
. /private/tmp/tertius-sweep-20261111/measure-lib.sh
D=scripts/temporal-upscale-timing.json
JOBS=${1:?usage: take7.sh <job list>}
. $JOBS
echo "take $(basename $JOBS) ended after $(( $(date +%s) - TAKE_START )) s"
