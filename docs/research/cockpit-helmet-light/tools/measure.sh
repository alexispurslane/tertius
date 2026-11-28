# measure.sh <dir> <frame> : mean 8-bit RGB of named regions
d=$1; f=$(printf %06d $2)
for reg in "panelOrange 120x130+770+65" "panelMid 200x120+950+120" "panelLow 200x60+1000+250" "cyanEdge 120x40+1120+330" "wallLeft 200x150+20+350" "floor 200x80+700+600"; do
  set -- $reg
  printf "%-12s " $1; magick "$d/cockpit-$f.png" -alpha off -crop $2 +repage -format "%[fx:mean.r*255] %[fx:mean.g*255] %[fx:mean.b*255]\n" info: | awk '{printf "%6.1f %6.1f %6.1f\n",$1,$2,$3}'
done
