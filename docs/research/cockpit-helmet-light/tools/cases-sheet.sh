# Diagnostic tool code. cases-sheet.sh <out.jpg> <cases root> <case>=<label>...
# A sheet with one column per case: on top the 120-frame mean with the normal helmet, below it the helmet's light x 16
# (mid-gray = 0), from the folders that variant-cases.sh writes.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
out=$1; root=$2; shift 2
top=(); bottom=()
for c in "$@"; do
  name=${c%%=*}; label=${c#*=}
  top+=("$root/$name/helmet-mean-normal.png" "$label")
  bottom+=("$root/$name/helmet-helmet-x16.png" "$label: helmet light x16")
done
bash "$here/sheet.sh" "$out" $# "${top[@]}" "${bottom[@]}"
