#!/usr/bin/env bash
# Timing of main (e5e1d1a85, bin-main) against commit A (fb18a44e0 as bin-A; with the two-step form, the copy made as
# bin-A3 used this script with A renamed) in one view: three takes each, in the order main A A main main A, 120 frames
# at 1920 x 1080 and the default render scale, --video none, --in-flight 2, exposure 4.08, under the caller's exclusive
# GPU lock. Usage: timing-views.sh <name> <script.json> [extra demo flags...]. Prints each take's frame span, its direct
# light pass median and the 1-minute load.
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7
name=$1; script=$2; shift 2
O=$R-evidence/step3/timing-views/$name; mkdir -p $O
echo "$name load before: $(uptime | sed 's/.*load averages*: //')"
for i in 1 2 3; do order="main A"; [ $i = 2 ] && order="A main"; for b in $order; do
  /bin/rm -rf $O/$b-$i
  nix develop $R --command /private/tmp/t1-gout/bin-$b/Tertius.Demo --script $script --headless --out $O/$b-$i \
    --size 1920x1080 --video none --in-flight 2 --exposure 4.08 "$@" > $O/$b-$i.log 2>&1
  direct=$(python3 -I -c "import json,sys; print([p['medianMs'] for p in json.load(open(sys.argv[1]))['passes'] if p['pass']=='direct light'][0])" $O/$b-$i/gpu-timings.json 2>/dev/null)
  echo "$name $b $i $(grep -o 'median [0-9.]* ms' $O/$b-$i.log) direct $direct | load $(uptime | sed 's/.*load averages*: //' | cut -d, -f1)"
done; done
