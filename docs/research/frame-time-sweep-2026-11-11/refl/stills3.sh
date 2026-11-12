#!/usr/bin/env bash
# Untimed work for the shared mode of the GPU lock, third round. It reads refl/stills3.jobs when it starts, so the list
# can grow while the take waits in the queue. Dev shell.
set -uo pipefail
S=/private/tmp/tertius-sweep-20261111
W=/Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0
R=$S/refl
cd $W
run() { # run <build> <script> <out name> [demo options]: one headless run of a script, skipped when its folder is complete
  local build=$1 script=$2 name=$3; shift 3
  local out=$R/out3/$name
  [ -f $out/done ] && return
  [ -x $S/bin-$build/Tertius.Demo ] || { echo "$name: no build $build"; return; }
  rm -rf $out; mkdir -p $out
  $S/bin-$build/Tertius.Demo --script $R/$script --headless --out $out "$@" > $out.log 2>&1; local code=$?
  [ $code = 0 ] && touch $out/done
  echo "$name: $(ls $out | wc -l) files, exit $code"
}
keep() { # keep <out name> <prefix> <frame>: of a film's frames, only the named one stays
  find $R/out3/$1 -name "$2-*.png" ! -name "$2-$3.png" -delete 2>/dev/null
}
mean() { # mean <out name> <prefix> <first frame>: the mean of a film's later frames as .npy, then the frames go
  python3 -I $S/tools/mean.py $R/out3/$1 $2 $3 && rm -f $R/out3/$1/$2-*.png
}
film() { # film <out name> <build> <script> [NAME=VALUE...]: a script's captures as one video, with extra variables
  local name=$1 out=$R/out3/$1 build=$2 script=$3; shift 3
  [ -f $out/done ] && return
  rm -rf $out; mkdir -p $out
  env "$@" $S/bin-$build/Tertius.Demo --script $R/$script --headless --out $out --video $out/film.mkv --fps 60 > $out.log 2>&1; local code=$?
  [ $code = 0 ] && touch $out/done
  echo "$name: $(ls -la $out/film.mkv 2>/dev/null | awk '{print $5}') bytes, exit $code"
}
mkdir -p $R/out3
cp $R/stills3.jobs $R/stills3.jobs.ran
. $R/stills3.jobs.ran
echo "stills 3 ended"
