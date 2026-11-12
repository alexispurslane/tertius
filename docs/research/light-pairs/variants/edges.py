"""Scratch tool of ticket t2-mlvy: the step of the candidate's light across the edges of 16-pixel render tiles, against
the step between other neighboring pixels. The stills are 1920 x 1080 outputs of a 1280 x 720 render, so a tile edge of
the render lies every 24 output pixels. The measure works on the ratio candidate / main of the display-linear luminance,
blurred by one output pixel, over a region of the image: a tile list that dropped a light for one tile and not for its
neighbor shows as a step of that ratio at the tile's edge.
Usage: edges.py <survey.py> <main.png> <candidate.png> <x0> <y0> <x1> <y1>"""
import importlib.util
import sys

spec = importlib.util.spec_from_file_location("survey", sys.argv[1])
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
import numpy as n
import scipy.ndimage as nd

main, cand = survey.luminance(sys.argv[2]), survey.luminance(sys.argv[3])
x0, y0, x1, y1 = map(int, sys.argv[4:8])
PERIOD = int(sys.argv[8]) if len(sys.argv) > 8 else 24
ratio = (nd.gaussian_filter(cand, 1.0) + 0.002) / (nd.gaussian_filter(main, 1.0) + 0.002)
ratio = ratio[y0:y1, x0:x1]
for axis, name, first in ((1, "columns", x0), (0, "rows", y0)):
    step = n.abs(n.diff(ratio, axis=axis))
    index = n.arange(step.shape[axis]) + first + 1          # the output pixel on the far side of each step
    edge = (index % PERIOD) == 0
    at_edge = n.compress(edge, step, axis=axis)
    inside = n.compress(~edge, step, axis=axis)
    print(f"{name}: step of the ratio at tile edges: mean {at_edge.mean() * 100:.4f} %, p99 {n.percentile(at_edge, 99) * 100:.4f} %, "
          f"max {at_edge.max() * 100:.4f} %; between other pixels: mean {inside.mean() * 100:.4f} %, "
          f"p99 {n.percentile(inside, 99) * 100:.4f} %, max {inside.max() * 100:.4f} %")
print(f"ratio over the region: min {ratio.min():.4f}, median {n.median(ratio):.4f}, max {ratio.max():.4f}")
