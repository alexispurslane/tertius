# codes.py <case> <variant...>: for each variant's long-mean reference PNG of the survey, the median gray code and the
# shares of pixels above the codes 240 and 250. It chose the cabin's fixed exposure.
import sys, numpy as np
from PIL import Image

case = sys.argv[1]
for v in sys.argv[2:]:
    a = np.asarray(Image.open(f's/{v}/images/still/{case}-reference.png').convert('L')).astype(float)
    print(f"{case:12s} {v:10s} median code {np.median(a):5.1f}   above 240 {100 * (a > 240).mean():5.1f}%   above 250 {100 * (a > 250).mean():5.1f}%")
