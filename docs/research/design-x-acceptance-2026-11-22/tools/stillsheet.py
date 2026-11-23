# Tool code of t0-5tyj. stillsheet.py <out.png> <case> <M run> <X run>: the sheet of a still where X scores worse than M.
# Top row: M's and X's still at draw 132, at half size. Bottom row: one 480 x 270 window at full resolution, from left to
# right M's still, X's still, M's long reference and X's long reference (each build's mean of its last captures, which
# the survey scores the still against, saved as gray sRGB luminance). The window is the one, on a grid of 30 output
# pixels, that holds the most of X's specks by the survey's own rule (speck_mask beyond SPECK_REFERENCE_REACH of a
# reference speck, over visible pixels). With no speck, it is the window where X's luminance exceeds its reference
# most. It prints the window and the specks in it. Run it through m.sh. It imports the checkout's tools/renderer-survey.py.
import importlib.util
import os
import sys
import numpy as np
from PIL import Image, ImageDraw

spec = importlib.util.spec_from_file_location('survey', os.path.join(os.path.dirname(os.path.abspath(__file__)), '../../../../tools/renderer-survey.py'))
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)

out, case, runs = sys.argv[1], sys.argv[2], dict(zip(('M', 'X'), sys.argv[3:5]))
W, H, STEP = 480, 270, 30


def path(run, reference=False):
    return f"s/{run}/images/still/{case}{'-reference' if reference else ''}.png"


def image(run, reference=False):
    return Image.open(path(run, reference)).convert('RGB')


def reference_luminance(run):
    a = np.asarray(Image.open(path(run, True)).convert('L'), dtype=np.float32) / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4).astype(np.float32)


def speck_centres(run):
    y, ref = survey.luminance(path(run)), reference_luminance(run)
    nd = survey.ndimage()
    reach = 2 * survey.SPECK_REFERENCE_REACH + 1
    real = nd.binary_dilation(survey.speck_mask(ref), structure=np.ones((reach, reach), dtype=bool))
    labels, count = nd.label(survey.speck_mask(y) & ~real & survey.visible(ref))
    return np.array(nd.center_of_mass(labels > 0, labels, range(1, count + 1))).reshape(-1, 2), y, ref


centres, y, ref = speck_centres(runs['X'])
weight = np.zeros(y.shape)
for cy, cx in centres:
    weight[int(cy), int(cx)] += 1
if not len(centres):
    weight = np.maximum(y - ref, 0)
integral = np.pad(weight, ((1, 0), (1, 0))).cumsum(0).cumsum(1)
best, at = -1.0, (0, 0)
for y0 in range(0, weight.shape[0] - H + 1, STEP):
    for x0 in range(0, weight.shape[1] - W + 1, STEP):
        s = integral[y0 + H, x0 + W] - integral[y0, x0 + W] - integral[y0 + H, x0] + integral[y0, x0]
        if s > best:
            best, at = s, (x0, y0)
x0, y0 = at
inside = [(int(cx), int(cy)) for cy, cx in centres if x0 <= cx < x0 + W and y0 <= cy < y0 + H]
print(f'{case}: X has {len(centres)} specks, window x {x0} to {x0 + W}, y {y0} to {y0 + H} holds {len(inside)}: {inside}')


def tile(im, label):
    t = Image.new('RGB', (im.width, im.height + 22), (0, 0, 0))
    t.paste(im, (0, 22))
    ImageDraw.Draw(t).text((4, 5), label, fill=(255, 255, 255))
    return t


top = [tile(image(runs[s]).resize((960, 540), Image.LANCZOS), f'{s}, {case}, draw 132, half size') for s in ('M', 'X')]
box = (x0, y0, x0 + W, y0 + H)
bottom = [tile(image(runs[s], ref).crop(box), f"{s} {'reference' if ref else 'still'}, x {x0}-{x0 + W}, y {y0}-{y0 + H}, 1:1")
          for ref in (False, True) for s in ('M', 'X')]
width = max(sum(t.width for t in top) + 6, sum(t.width for t in bottom) + 18)
sheet = Image.new('RGB', (width, top[0].height + bottom[0].height + 6), (60, 60, 60))
for i, t in enumerate(top):
    sheet.paste(t, (i * (t.width + 6), 0))
for i, t in enumerate(bottom):
    sheet.paste(t, (i * (t.width + 6), top[0].height + 6))
sheet.save(out)
print(out, sheet.size)
