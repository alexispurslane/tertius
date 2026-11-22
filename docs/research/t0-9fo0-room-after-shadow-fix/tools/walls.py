# walls.py <variant>...: the light of the upper side walls of rect-lamps, in the survey's display-linear luminance
# (tools/renderer-survey.py, luminance), in each variant's shown still and in its own build's long reference (the mean
# of the survey's last 20 captures, images/still/rect-lamps-reference.png). The boxes are output pixels. "mid": the
# left wall above the windows (x 60 to 400, y 300 to 500) and its mirror on the right wall (x 1520 to 1860). "top":
# the left wall just below the ceiling's seam in the near corner (x 20 to 200, y 150 to 300) and its mirror (x 1720 to
# 1900). "edge": the image's outer 100 columns, y 100 to 500, left and right. It prints each pair's means and
# left / right, so that a one-sided gradient shows as a ratio below the other builds'. The last column compares the
# builds directly: the left box against the same box and image of the first variant.
# Run it through m.sh.
import importlib.util
import sys
from pathlib import Path

spec = importlib.util.spec_from_file_location('survey', Path(__file__).resolve().parents[4] / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
BOXES = {'mid': ((slice(300, 500), slice(60, 400)), (slice(300, 500), slice(1520, 1860))),
         'top': ((slice(150, 300), slice(20, 200)), (slice(150, 300), slice(1720, 1900))),
         'edge': ((slice(100, 500), slice(0, 100)), (slice(100, 500), slice(1820, 1920)))}
print(f'{"variant":8} {"image":10} {"box":4} {"left":>8} {"right":>8} {"left/right":>10} {"left/first":>10}')
first = {}
for v in sys.argv[1:]:
    for name, file in (('still', 'rect-lamps.png'), ('reference', 'rect-lamps-reference.png')):
        y = survey.luminance(Path(f's/{v}/images/still') / file)
        for box, (lb, rb) in BOXES.items():
            left, right = float(y[lb].mean()), float(y[rb].mean())
            first.setdefault((name, box), left)
            print(f'{v:8} {name:10} {box:4} {left:8.5f} {right:8.5f} {left / right:10.4f} {left / first[(name, box)]:10.4f}')
