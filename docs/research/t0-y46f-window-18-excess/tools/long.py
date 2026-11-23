# long.py <out> <case> <captures>: one survey still with <captures> captures 33 draws apart in place of the survey's 28,
# rendered by the scratch probe <out>/probe-bin under the shared GPU lock, as run_still of tools/renderer-survey.py does.
# It writes <out>/images/still/<case>.png (the capture at draw 132) and <case>-reference.png (the mean of the last 20
# captures), so that light.py, whole.py and lengths.py read it like a survey run. Run it inside the dev shell from the
# checkout's root. The environment switches pass through to the probe.
import importlib.util, shutil, sys
from pathlib import Path

ROOT = Path.cwd()
spec = importlib.util.spec_from_file_location("survey", ROOT / "tools" / "renderer-survey.py")
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
out, name, count = Path(sys.argv[1]).resolve(), sys.argv[2], int(sys.argv[3])
case = next(c for c in survey.load_cases()["stills"] if c["name"] == name)
source, frame = survey.source_of(case)
script, first = survey.views_script(source, {frame: survey.captures("v", count)})
index = survey.still_capture_index(first[frame])
work = out / "work"
survey.render(work, script, binary=survey.probe_binary(out), env=survey.STILL_CONTINUES, extra=survey.NO_BLOOM)
n = survey.np()
reference = sum(survey.luminance(work / f"v-s{k:02d}.png") for k in range(count - survey.REFERENCE_CAPTURES, count))
reference /= survey.REFERENCE_CAPTURES
images = out / "images" / "still"; images.mkdir(parents=True, exist_ok=True)
shutil.copy(work / f"v-s{index:02d}.png", images / f"{name}.png")
from PIL import Image
srgb = n.where(reference <= 0.0031308, reference * 12.92, 1.055 * n.power(reference, 1 / 2.4) - 0.055)
Image.fromarray((n.clip(srgb, 0, 1) * 255 + 0.5).astype(n.uint8)).save(images / f"{name}-reference.png")
first_draw = survey.still_draws(first[frame], count - survey.REFERENCE_CAPTURES)
last_draw = survey.still_draws(first[frame], count - 1)
print(f"{name}: {count} captures, shown draw {survey.still_draws(first[frame], index)}, long mean of draws {first_draw} to {last_draw}")
shutil.rmtree(work)
