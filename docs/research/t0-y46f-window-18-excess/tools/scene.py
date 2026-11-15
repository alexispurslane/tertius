# scene.py <out> <case>: one survey still of the scratch probe <out>/probe-bin in scene light, under the shared GPU
# lock. It renders with the survey's PROBE_OPTIONS, so each capture holds log2 scene luminance in 24 bits
# (renderer-survey.py, probe_luminance). It writes <out>/scene/<case>-shown.npy (the capture at draw 132) and
# <case>-long.npy (the mean scene luminance of the last 20 captures, draws 297 to 924), float32 at 1920 x 1080. Run it
# inside the dev shell from the checkout's root. The environment switches pass through to the probe.
import importlib.util, shutil, sys
from pathlib import Path

ROOT = Path.cwd()
spec = importlib.util.spec_from_file_location("survey", ROOT / "tools" / "renderer-survey.py")
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
out, name = Path(sys.argv[1]).resolve(), sys.argv[2]
case = next(c for c in survey.load_cases()["stills"] if c["name"] == name)
script, first = survey.still_script(*survey.source_of(case))
index = survey.still_capture_index(first)
work = out / "work"
survey.render(work, script, binary=survey.probe_binary(out), env=survey.STILL_CONTINUES, extra=survey.PROBE_OPTIONS)
n = survey.np()
count = survey.STILL_CAPTURES
long = sum(survey.probe_luminance(work / f"v-s{k:02d}.png") for k in range(count - survey.REFERENCE_CAPTURES, count))
long /= survey.REFERENCE_CAPTURES
(out / "scene").mkdir(parents=True, exist_ok=True)
n.save(out / "scene" / f"{name}-long.npy", long.astype(n.float32))
n.save(out / "scene" / f"{name}-shown.npy", survey.probe_luminance(work / f"v-s{index:02d}.png").astype(n.float32))
print(f"{name}: shown draw {survey.still_draws(first, index)}, long mean of draws "
      f"{survey.still_draws(first, count - survey.REFERENCE_CAPTURES)} to {survey.still_draws(first, count - 1)}")
shutil.rmtree(work)
