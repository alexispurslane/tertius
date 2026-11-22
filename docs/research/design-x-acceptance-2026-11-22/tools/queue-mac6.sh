#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-mac6.sh: the Mac's picture runs and fixed-scale takes after the probe's lamp switch
# (TERTIUS_PROBE_LAMP_GLOW), with the playback probes of playbuild.sh. None of them depends on the frame time, so they
# take the GPU lock in its shared mode and run at any load.
#   1. The check that GLOW=stays gives the old switch's pictures: the old X probe ($BASE/x) against the new one at the
#      fixed 2/3, film frames 150, 220 and 380, into $BASE/macdyn/stayscheck/{old,new}.
#   2. The fair cabin switch (GLOW=off, the default) at the fixed 2/3 on M and X: the survey's take, runs fair-M, fair-X.
#   3. The jambs: picture runs of the switch (GLOW=off) at the six moments, on M and X, with the render scale played back
#      from the card's logged sequence at 16.67 ms, then constant at 0.956, then constant at 2/3; and the clamps-off
#      reference (TERTIUS_A_NO_CLAMP=1, X) at 0.956 and at 2/3.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd)
R=$(cd "$T/../../../.." && pwd)
export BASE=/private/tmp/t0-5tyj
B=$BASE
cd "$R"
[ -f $B/macdyn/switch.json ] || bash "$T/macdyn.sh" none switch m 2>/dev/null
O=$B/macdyn/stayscheck; rm -rf $O; mkdir -p $O/old $O/new
for v in old new; do
  bin=$([ $v = old ] && echo $B/x/probe-bin || echo $B/xplay/probe-bin)
  env TERTIUS_PROBE_LAMP_SEL=z:-12:4 TERTIUS_PROBE_LAMP_OFF=193-393 TERTIUS_PROBE_LAMP_GLOW=stays TERTIUS_PROBE_EXPOSURE=4.08 \
    TERTIUS_PROBE_SERIES=$O/$v.series TERTIUS_PROBE_KEEP=f-000150,f-000220,f-000380 \
    nix develop --command bash tools/gpu-lock.sh --shared $O/$v.lock.log $bin/Tertius.Demo --script $B/macdyn/switch.json \
    --headless --out $O/$v --size 1920x1080 > $O/$v.log 2>&1
  echo "$(date +%T) stays check $v exit $?"
done
PROBE=$B/mplay/probe-bin bash "$T/take.sh" fair-M switch cabin
PROBE=$B/xplay/probe-bin bash "$T/take.sh" fair-X switch cabin
export KEEP=f-000150,f-000180,f-000220,f-000355,f-000380,f-000455
C=$B/cloud/sizes/out/sizes
for side in x m; do
  bash "$T/macdyn.sh" pictures switch $side $C/switch-$side-16.6667/size-193-393.txt jamb-$side-card16
done
for scale in 0.956 0.6667; do
  for side in x m; do bash "$T/macdyn.sh" pictures switch $side $scale jamb-$side-$scale; done
  bash "$T/macdyn.sh" pictures switch x $scale jamb-a-$scale TERTIUS_A_NO_CLAMP=1
done
echo "$(date +%T) queue-mac6 done"
