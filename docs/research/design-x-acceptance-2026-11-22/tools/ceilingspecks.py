# Tool code of t0-5tyj. ceilingspecks.py <label=run folder>...: the specks of each cabin still in the top third of the
# view, where the cabin's ceiling is, by the survey's own speck rule (tools/renderer-survey.py, specks). It reads the
# still and its reference from <run>/images/still/<case>.png and <case>-reference.png. Per case and run: the count of
# specks in the whole view and in the top third, and the whole view's specks per 10,000 visible pixels, which the
# survey's result also gives (a check that the saved pictures measure as the run did).
# Run it in the dev shell: nix develop --command python3 -I <this file> M=<run> X=<run> ...
import importlib.util
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('survey', ROOT / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
nd = survey.ndimage()


def counts(still, reference):
    reach = 2 * survey.SPECK_REFERENCE_REACH + 1
    real = nd.binary_dilation(survey.speck_mask(reference), structure=survey.np().ones((reach, reach), dtype=bool))
    labels, whole = nd.label(survey.speck_mask(still) & ~real & survey.visible(reference))
    top = len(set(survey.np().unique(labels[: labels.shape[0] // 3])) - {0})
    return whole, top


print(f"{'case':20s} {'run':12s} {'whole':>6s} {'top third':>10s} {'per 10k (saved)':>16s} {'per 10k (run)':>14s}")
for arg in sys.argv[1:]:
    label, folder = arg.split('=', 1)
    pictures = Path(folder, 'images', 'still')
    for png in sorted(pictures.glob('*cabin.png')):
        case = png.stem
        still = survey.luminance(png)
        reference = survey.luminance(pictures / f'{case}-reference.png')
        whole, top = counts(still, reference)
        saved = survey.specks(still, reference)
        result = Path(folder, 'results', 'still', f'{case}.json')
        run = json.loads(result.read_text())['specks'] if result.exists() else float('nan')
        print(f"{case:20s} {label:12s} {whole:6d} {top:10d} {saved:16.4f} {run:14.4f}")
