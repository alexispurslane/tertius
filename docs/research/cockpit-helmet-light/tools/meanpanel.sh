# meanpanel.sh <dir> <out.png>: mean of frames 0-119 over the canopy crop 560x315+688+4
d=$1; out=$2
magick $(ls $d/cockpit-0000*.png $d/cockpit-0001[01]*.png | head -120) -crop 560x315+688+4 +repage -evaluate-sequence mean "$out"
