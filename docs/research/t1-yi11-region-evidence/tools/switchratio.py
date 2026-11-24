# Tool code of t1-yi11. switchratio.py <column>...: the fair cabin switch's linear curves from settle-<column>.json (the
# survey's switch series: per film draw 0 to 459, the mean linear light of the whole picture and of its ten regions,
# under the survey's options bloom off and the Hable tone curve). Index 0 of a row is the whole picture, and the
# survey's changed regions index the same row. For each column it gives the switch run against its
# control, switch / control - 1, where the control is the run with the lamps always off for draws 160 to 359 (after
# off) and the run with the lamps always on for draws 360 to 459 (after on). It prints the whole picture at the README's
# draws and, for the windows after each cause leaves the rays (film draws 205 to 295 after off, 405 to 459 after on),
# the largest |ratio| of the whole picture and of any region of the survey's changed regions. It writes
# switch-ratio.json beside the record, with every column's per-draw ratios.
import json
import sys
from pathlib import Path

REC = Path(__file__).resolve().parent.parent
OFF, ON = 160, 360
columns = sys.argv[1:]
out = {}
for col in columns:
    s = json.loads((REC / f'settle-{col}.json').read_text())
    sw, off, on = (s['series'][k] for k in ('switch', 'off', 'on'))
    ratio = [[sw[d][i] / (off if d < ON else on)[d][i] - 1 for i in range(len(sw[d]))] if d >= OFF else None
             for d in range(len(sw))]
    out[col] = {'regions': s['regions'], 'ratio': ratio, 'offDraws': s['offDraws'], 'offDrawsWhole': s['offDrawsWhole'],
                'onDraws': s['onDraws'], 'onDrawsWhole': s['onDrawsWhole']}
(REC / 'switch-ratio.json').write_text(json.dumps(out))
draws = [200, 205, 230, 260, 295, 300, 359, 380, 400, 405, 420, 459]
print('| column | ' + ' | '.join(f'{d - OFF:+d} off' if d < ON else f'{d - ON:+d} on' for d in draws) + ' |')
for col in columns:
    print(f'| {col} | ' + ' | '.join(f"{out[col]['ratio'][d][0]:+.3f}" for d in draws) + ' |')
for lo, hi, name in ((205, 296, 'film draws 205 to 295'), (405, 460, 'film draws 405 to 459')):
    for col in columns:
        r = out[col]['ratio']
        whole = max(abs(r[d][0]) for d in range(lo, hi))
        region = max(abs(r[d][i]) for d in range(lo, hi) for i in out[col]['regions'])
        print(f'{name}, {col}: largest |switch / control - 1| whole {whole:.3f}, any changed region {region:.3f}')
