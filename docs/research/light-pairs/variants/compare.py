"""Scratch tool of ticket t2-mlvy: compares a still of a candidate build with the same still of main, with the measures
of tools/renderer-survey.py. Usage: compare.py <survey.py> <main.png> <candidate.png> [<heat.png>]"""
import importlib.util
import sys

spec = importlib.util.spec_from_file_location("survey", sys.argv[1])
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
import numpy as n
from PIL import Image

main, cand = survey.luminance(sys.argv[2]), survey.luminance(sys.argv[3])
mask = survey.visible(main)
lvl = survey.level(cand, main)
score, rms = survey.blotch(cand, main)
b0, b1 = survey.blur(main), survey.blur(cand)
rel = (b1 - b0) / (b0 + 0.01)
raw = n.abs(cand - main)
a = n.asarray(Image.open(sys.argv[2]).convert("RGB"), dtype=n.int16)
c = n.asarray(Image.open(sys.argv[3]).convert("RGB"), dtype=n.int16)
steps = n.abs(a - c).max(axis=2)
print(f"level {lvl * 100:+.2f} %  blotch p90 {score * 100:.2f} %  rms {rms * 100:.2f} %  "
      f"blurred relative change: median {n.median(rel[mask]) * 100:+.2f} %, p1 {n.percentile(rel[mask], 1) * 100:+.2f} %, "
      f"p99 {n.percentile(rel[mask], 99) * 100:+.2f} %, min {rel[mask].min() * 100:+.2f} %  "
      f"8-bit steps: mean {steps.mean():.2f}, p99 {n.percentile(steps, 99):.0f}, max {steps.max()}  "
      f"pixels changed by more than 2 steps {100.0 * (steps > 2).mean():.2f} %")
if len(sys.argv) > 4:
    heat = n.clip(n.abs(rel) / 0.10, 0, 1)
    rgb = n.stack([heat, 0.25 * (1 - heat) * n.clip(b0 * 4, 0, 1), (rel < 0) * heat * 0 + 0.25 * (1 - heat) * n.clip(b0 * 4, 0, 1)], axis=2)
    Image.fromarray((rgb * 255).astype(n.uint8)).save(sys.argv[4])
