# Tool code of t0-gumm. cardstart.py <card folder>: the render scale of the fair test's card job (cardfair.sh: the bay's
# frozen start, 200 draws) per run: the lowest scale of draws 0 to 59, the rest (the median of draws 150 to 199), and the
# draws under the rest - 0.02 (the last such draw and their count), as cardreturn.py counts them for the switch and the cut.
import sys
from pathlib import Path
import numpy as np

for folder in sorted(Path(sys.argv[1]).iterdir()):
    scale = {}
    for line in open(next(folder.glob('size-*.txt'))):
        scale.setdefault(int(line.split()[0]), float(line.split()[3]))
    rest = float(np.median([scale[d] for d in range(150, 200)]))
    under = [d for d in range(0, 200) if scale[d] < rest - 0.02]
    print(f'{folder.name}: lowest {min(scale[d] for d in range(60)):.3f} at draw {min(range(60), key=lambda d: scale[d])}, '
          f'rest {rest:.3f}, last draw under the rest - 0.02: {max(under) if under else 0}, count {len(under)}')
