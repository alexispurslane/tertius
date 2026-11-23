# Tool code of t0-xwdz. tailtable.py <run folder> [first] [last]: the per-draw table of one take's tail.txt
# (scratch-tail-log.patch) from the last reset of the run on, draws <first> to <last> after it (default 0 to 15), as a
# Markdown table. Shares are of the surface pixels of the draw, in percent. The columns:
#   draw: the draw index since the reset;
#   reproj < 8: the reflection history's length after the reprojection is under FullRateFrames (8), as the plan reads it;
#   after < 8: the length after this draw's anti-lag is under 8;
#   cut: the anti-lag shortened the length in this draw (by more than 1e-3); "cut young": of them from a length under 8;
#   to 4: the cut ended at the fast cap of 4 or less;
#   behind: the length after this draw is under draw index + 1 - 0.01, so some cut or loss happened since the reset;
#   own: the reflection own rays of the draw, in percent of the surface pixels and as a count;
#   then the own rays by why the plan saw a length under 8 (counts): "round": the pixel's own length and every weighted
#   tap is 8 or more, and only the float rounding of the weighted mean falls under 8; "nbr": the own length is 8 or more
#   and a weighted tap is under 8; "short": the own length is under 8; ">= 8": the reprojected length is 8 or more (the
#   block's ray does not fit the pixel's lobe).
import sys
from pathlib import Path

folder = Path(sys.argv[1])
first = int(sys.argv[2]) if len(sys.argv) > 2 else 0
last = int(sys.argv[3]) if len(sys.argv) > 3 else 15
rows = [list(map(int, line.split())) for line in (folder / 'tail.txt').read_text().splitlines()]
reset = max(i for i, r in enumerate(rows) if r[1] == 0)
print(f'{folder.name}: reset at frame {rows[reset][0]}, render size {rows[reset][2]} x {rows[reset][3]}')
print()
print('| draw | reproj < 8 | after < 8 | cut | cut young | to 4 | behind | own | own count | round | nbr | short | >= 8 | diffuse own |')
print('|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
for r in rows[reset:]:
    d = r[1]
    if d < first or d > last:
        continue
    c = r[4:]
    s = c[0]
    pct = lambda v: f'{100 * v / s:.1f}'
    own, fit, rnd, nbr, short = c[12], c[13], c[14], c[15], c[16]
    print(f'| {d} | {pct(c[1])} | {pct(c[3])} | {pct(c[5])} | {pct(c[6])} | {pct(c[7])} | {pct(c[8])} | {pct(own)} | {own} '
          f'| {rnd} | {nbr} | {short} | {fit} | {c[-1]} |')
