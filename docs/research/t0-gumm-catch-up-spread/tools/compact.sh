#!/usr/bin/env bash
# Tool code of t0-gumm. compact.sh: keeps the disk under the limit while a Mac queue (queue-mac*.sh) runs. Every minute it
# takes each finished picture run of macrun.sh in /private/tmp/t0-gbgj/gumm-mac/<event>-<code> (its size log complete, no
# Tertius.Demo writing into it, no events.txt yet), and
#   1. writes events.txt: the measures of events.py for its event (the folder name up to the first "-");
#   2. packs its kept PNGs, in draw order, into frames.mp4 (libx264, yuv444p, crf 8, one frame per draw) and lists their
#      names in frames.txt, for the three-pane videos;
#   3. deletes the PNGs.
# It ends when no queue runs and no finished run is left.
N=$(cd "$(dirname "$0")/.." && pwd); R=$(cd "$N/../../.." && pwd); O=/private/tmp/t0-gbgj/gumm-mac
declare -A DRAWS=([switch]=493 [start]=200 [cut]=334)
while true; do
  left=0
  for dir in $O/switch* $O/start* $O/cut*; do
    [ -d $dir ] || continue
    name=$(basename $dir); event=${name%%-*}; base=${event%+*}
    [ -f $dir/events.txt ] && continue
    ls $dir/*.png > /dev/null 2>&1 || continue
    if [ "$(cat $dir/size-*.txt 2>/dev/null | wc -l)" -lt ${DRAWS[$base]} ] || pgrep -f -- "--out $dir " > /dev/null; then left=1; continue; fi
    (cd $dir && ls a-*.png x-*.png b-*.png f-*.png 2>/dev/null) > $dir/frames.txt
    (cd "$R" && nix develop --command bash -c "python3 $N/tools/events.py '$name|$dir|$event' > $dir/events.tmp && \
      cd $dir && sed 's/^/file /' frames.txt > concat.txt && \
      ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -r 30 -i concat.txt -c:v libx264 -preset slow -crf 8 \
        -pix_fmt yuv444p -r 30 frames.mp4" 2>&1 | grep -v -i 'warning\|sqlite')
    if [ -s $dir/events.tmp ] && [ -s $dir/frames.mp4 ]; then
      /bin/mv -f $dir/events.tmp $dir/events.txt; rm -f $dir/*.png $dir/concat.txt
      echo "$(date +%T) $name: $(cat $dir/events.txt | cut -c1-200)"
    else
      echo "$(date +%T) $name: failed, PNGs kept"; touch $dir/events.txt
    fi
  done
  if [ $left = 0 ] && ! pgrep -f queue-mac > /dev/null; then echo "$(date +%T) done"; exit 0; fi
  sleep 60
done
