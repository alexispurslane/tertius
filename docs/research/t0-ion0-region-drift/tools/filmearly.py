# filmearly.py <out> <case>: the survey's motion part (renderer-survey.py, run_motion) for one film, with three more
# sampled frames: 10, 20 and 30, beside the middle and the last frame. Each sampled frame is set against a still of
# its own state, as the survey does, and its level is the frame's light over that still's, minus 1. The fixed exposure
# comes from the middle and the last frame only, as in the survey, so the middle and last levels repeat its numbers.
# It writes <out>/results/early/<case>.json ({label: {"frame", "level", "blotch"}}) and keeps the sampled frames and
# their stills in <out>/images/early/. Run it inside the dev shell from the checkout's root. The environment switches
# pass through to the probe. The survey's render takes the shared GPU lock.
import importlib.util, json, shutil, sys
from pathlib import Path

ROOT = Path.cwd()
spec = importlib.util.spec_from_file_location("survey", ROOT / "tools" / "renderer-survey.py")
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
EARLY = {"f10": 10, "f20": 20, "f30": 30}
out, name = Path(sys.argv[1]).resolve(), sys.argv[2]
case = next(c for c in survey.load_cases()["motion"] if c["name"] == name)
motion = survey.motion_case(case)
stops = survey.film_exposure(out, name, motion)
samples = {**EARLY, **motion.samples}
references, names = survey.motion_references_script(motion.film, list(samples.values()))
fixed = {"TERTIUS_PROBE_EXPOSURE": f"{stops:.4f}"}
reference_work = out / "work" / "early" / f"{name}-references"
survey.render(reference_work, references, binary=survey.probe_binary(out), env={**survey.STILL_CONTINUES, **fixed},
              extra=survey.NO_BLOOM)
work = out / "work" / "early" / name
survey.render(work, motion.film, binary=survey.probe_binary(out), env=fixed, extra=survey.NO_BLOOM)
files = sorted(work.glob("m*-*.png"))
images = out / "images" / "early"
result = {"stops": stops}
for label, k in samples.items():
    y, r = survey.luminance(files[k]), survey.luminance(reference_work / f"{names[k]}.png")
    score, _ = survey.blotch(y, r)
    result[label] = {"frame": k, "level": survey.level(y, r), "blotch": score}
    survey.keep_png(files[k], images / f"{name}-{label}.png")
    survey.keep_png(reference_work / f"{names[k]}.png", images / f"{name}-{label}-still.png")
(out / "results" / "early").mkdir(parents=True, exist_ok=True)
(out / "results" / "early" / f"{name}.json").write_text(json.dumps(result, indent=1) + "\n")
print(f"{name}: " + "  ".join(f"{label} {result[label]['level']:+.2f}" for label in samples))
shutil.rmtree(out / "work" / "early")
