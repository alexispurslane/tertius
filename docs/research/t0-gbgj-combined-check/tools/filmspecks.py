# Tool code of t0-gbgj. filmspecks.py [--case <case>] <run>...: t0-xdck's filmspecks.py for any motion case (default
# rect-bay): the survey's speck count (tools/renderer-survey.py, specks: ter-9371's rule, per 10,000 visible pixels,
# without the specks that the reference holds) of the film's middle and last frame, each against the still of the same
# state that the survey sets the frame against (images/motion/<case>-<sample>-still.png). m.sh runs it in $W/w.
import importlib.util
import sys
from pathlib import Path

survey_path = Path(__file__).resolve().parents[4] / 'tools' / 'renderer-survey.py'
spec = importlib.util.spec_from_file_location('survey', survey_path)
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
args = sys.argv[1:]
case = 'rect-bay'
if args[:1] == ['--case']:
    case, args = args[1], args[2:]
print(f'{"run":12} {case} specks middle / last')
for v in args:
    counts = []
    for sample in ('middle', 'end'):
        folder = Path(f's/{v}/images/motion')
        counts.append(survey.specks(survey.luminance(folder / f'{case}-{sample}.png'),
                                    survey.luminance(folder / f'{case}-{sample}-still.png')))
    print(f'{v:12} {counts[0]:6.2f} / {counts[1]:6.2f}')
