# whole.py <truth> <variant...>: the scene light of each variant's long reference (the mean of the survey still's last
# 20 captures) over every pixel of the image, without light.py's cut to smooth pixels, as a share of the truth's. A
# filter that only moves light between pixels keeps this share; light.py's regions can still gain. The share of pixels
# at the code 255 in any channel is printed too, because the inverse of the tone curve cannot read past it.
import sys, numpy as np
from PIL import Image
from light import scene

truth, variants = sys.argv[1], sys.argv[2:]
case = 'rect-lamps' if len(truth.split(':')) == 1 else truth.split(':')[1]
truth = truth.split(':')[0]
t = scene(f's/{truth}/images/still/{case}-reference.png')
for v in variants:
    path = f's/{v}/images/still/{case}-reference.png'
    r = scene(path)
    clipped = (np.asarray(Image.open(path).convert('RGB')) == 255).any(axis=2).mean()
    print(f"{case:12s} {v:16s} whole image {100 * r.mean() / t.mean():6.1f}%   clipped {100 * clipped:5.2f}%")
