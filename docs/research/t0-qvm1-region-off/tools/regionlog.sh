#!/usr/bin/env bash
# Tool code of the region take (t0-qvm1), its second step: the region test's log, no rule. Survey runs on xfd (Design X
# 05a27262d with region evidence) at the fixed render scale 2/3, through t0-gbgj's take.sh, with t0-3vwx's region log on
# (TERTIUS_X_REGION_LOG=<run>/region: per draw and anchor the region's light, its count of valid draws and the test's
# answer). The runs: g-exit (the still exit-cabin), g-bay (the motion film rect-bay), g-lag (the lag film bay-sun) and
# g-switch (the fair lamp switch, whose control with the lamps on through the film is the still cabin). A run whose
# survey wrote a result is kept. take.sh exits 0 also on a failed run, so each run is checked by its result file. Each
# run starts at 22 GiB of free disk or more (it waits for that), and the script stops under 20 GiB.
G=$(cd "$(dirname "$0")/../../t0-gbgj-combined-check/tools" && pwd); W=/private/tmp/t0-gbgj
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
for run in g-exit:still:exit-cabin g-bay:motion:rect-bay g-lag:lag:bay-sun g-switch:switch:cabin; do
  IFS=: read name part case <<< "$run"
  [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
  until [ "$(freegb)" -ge 22 ]; do sleep 60; done
  bash "$G/take.sh" $name $part $case xfd TERTIUS_X_RENDER_SCALE=0.6667 TERTIUS_X_REGION_LOG=$W/runs/$name/region
  compgen -G "$W/runs/$name/results/*/*.json" > /dev/null || echo "$(date +%T) $name: no result, see $W/runs/$name/survey.log"
done
echo "$(date +%T) regionlog.sh done"
