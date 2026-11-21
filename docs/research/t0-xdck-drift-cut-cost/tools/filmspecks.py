# filmspecks.py <variant...>: the survey's speck count (tools/renderer-survey.py, specks: ter-9371's rule, per 10,000
# visible pixels, without the specks that the reference holds) of the film rect-bay's middle and last frame, each
# against the still of the same state that the survey sets the frame against (images/motion/rect-bay-<sample>-still.png).
# The survey counts specks only in stills, so this script applies its function to the film's two sampled frames.
# Run it through m.sh, which runs in /private/tmp/t0-xdck/w with s/<variant> links.
import importlib.util
import sys
from pathlib import Path

survey_path = Path(__file__).resolve().parents[4] / 'tools' / 'renderer-survey.py'
spec = importlib.util.spec_from_file_location('survey', survey_path)
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)

print(f'{"variant":12} specks middle / last')
for v in sys.argv[1:]:
    counts = []
    for sample in ('middle', 'end'):
        folder = Path(f's/{v}/images/motion')
        frame = survey.luminance(folder / f'rect-bay-{sample}.png')
        still = survey.luminance(folder / f'rect-bay-{sample}-still.png')
        counts.append(survey.specks(frame, still))
    print(f'{v:12} {counts[0]:6.2f} / {counts[1]:6.2f}')
