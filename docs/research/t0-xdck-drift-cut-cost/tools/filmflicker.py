# filmflicker.py <variant...>: the survey's flicker of the film rect-bay (tools/renderer-survey.py, flicker): the
# low-frequency flicker and the grain flicker of single pixels, the median over the film's frames of the second
# difference relative to the three frames' mean, from runs/<variant>/results/motion/rect-bay.json. The survey's limits
# are 0.006 and 0.05. Run it with python3 -I from any folder.
import json
import sys
from pathlib import Path

RUNS = Path('/private/tmp/t0-xdck/runs')
print(f'{"variant":12} {"flicker":>8} {"grain":>8}')
for v in sys.argv[1:]:
    r = json.loads((RUNS / v / 'results' / 'motion' / 'rect-bay.json').read_text())
    print(f'{v:12} {r["flicker"]:8.5f} {r["grainFlicker"]:8.5f}')
