#!/usr/bin/env bash
# Tool code of t0-gbgj. wobtake.sh: the wobble's before and after on the Mac. wobfilm.py on m2r (main 284025851, before)
# and on wr (the wobble fix's branch 839bce1b5, after), both with the fair-switch probe and scratch-dynamic-scale.patch,
# both playing back the same size log of the card: the Design X side at the 10 ms target, scales 0.643 to 0.756 with
# 84 size changes (/private/tmp/t0-5tyj/cloud/sizes/out/sizes/switch-x-10/size-193-393.txt, copied to $W).
. "$(dirname "$0")/vars.sh"
T=$(cd "$(dirname "$0")" && pwd); R=$(git -C "$T" rev-parse --show-toplevel)
log=$W/size-card-switch-x-10.txt
[ -f $log ] || /bin/cp /private/tmp/t0-5tyj/cloud/sizes/out/sizes/switch-x-10/size-193-393.txt $log
for side in before:m2r after:wr; do name=wob-${side%%:*}; build=${side##*:}; out=$W/runs/$name
  if [ -e "$out/frames/f-000459.png" ]; then echo "$(date +%T) kept $name"; continue; fi
  mkdir -p "$out"; ln -sfn "$W/bins/$build/probe-bin" "$out/probe-bin"
  echo "$(date +%T) start $name $build (load $(sysctl -n vm.loadavg))"
  (cd "$R" && TERTIUS_X_SIZE_REPLAY=$log nix develop --command python3 -I "$T/wobfilm.py" "$out" > "$out/survey.log" 2>&1)
  echo "$(date +%T) end $name exit $? $(tail -1 "$out/survey.log" | cut -c1-160)"
done
echo "$(date +%T) wobtake done"
