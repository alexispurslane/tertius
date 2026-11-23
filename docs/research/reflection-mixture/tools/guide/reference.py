# Tool code of t1-nyjl: the independent reference of the bay's daub. The mean, in linear light, of captures 30 to 99 of
# a 100-capture take of the frozen bay at share 0.25 (draws 990 to 3,299): their random numbers come from draw indices
# that no compared still used (those end at draw 659), so the reference holds none of their noise. Writes
# <daub folder>/reference-mean.npy. Usage: python3 -I reference.py <reference take> <daub folder>
import sys
from pathlib import Path
import numpy as np
from PIL import Image
take, out = Path(sys.argv[1]), Path(sys.argv[2])
caps = [take / f'c-s{i:02d}.png' for i in range(30, 100)]
def lin(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float64) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
np.save(out / 'reference-mean.npy', (sum(lin(p) for p in caps) / len(caps)).astype(np.float32))
print('captures in the reference:', len(caps))
