# Tool code of the region cap's takes (t0-qvm1). early.py: the survey's blotch and level of the still.py captures 1 to 5,
# 8 and 26 against the mean of captures 8 to 27, per view and build. Run it in the dev shell from the checkout's root.
import importlib.util
from pathlib import Path
spec = importlib.util.spec_from_file_location('survey', Path('tools/renderer-survey.py'))
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
R = Path('/private/tmp/t0-qvm1c/runs')
for view in ('lamp', 'cabin', 'bay', 'cabinoff'):
    for b in ('pm', 'pc'):
        f = R / f'{b}-{view}' / 'frames'
        ref = sum(survey.luminance(f / f'v-s{i:02d}.png') for i in range(8, 28)) / 20
        row = []
        for c in (1, 2, 3, 4, 5, 8, 26):
            y = survey.luminance(f / f'v-s{c:02d}.png')
            s, r = survey.blotch(y, ref)
            row.append(f'{c}:{s:.4f}/{100*survey.level(y, ref):+.2f}%')
        print(view, b, ' '.join(row))
