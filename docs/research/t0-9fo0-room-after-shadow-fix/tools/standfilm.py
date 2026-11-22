# Tool code of t0-9fo0. standfilm.py <out>: the stand view as a film with every light signal, measured by the survey's
# own motion part (tools/renderer-survey.py, run_motion): the camera person in scene orbit from world tick 60, 600
# frames of one tick each, as the survey's stand case "cabin-stand" (cases.json) runs it, so that the frames hold world
# ticks 61 to 660 and the sun's step at ticks 300 to 306. The motion part gives the film's flicker and grain flicker,
# the level and blotch of its middle and last frame against stills of the same states at one fixed exposure, and keeps
# the start, middle and last frames. The stand part itself shows the bounce light alone and keeps no frame. <out> holds
# probe-bin, as take.sh makes it. Run it in the dev shell from the checkout's root.
import importlib.util
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('survey', root / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
case = {'name': 'cabin-stand-film', 'scene': 'orbit', 'camera': 'person', 'ticks': 60, 'frames': 600, 'view': 'fixed'}
result = survey.run_motion(Path(sys.argv[1]).resolve(), case, None)
print({k: result[k] for k in ('frames', 'stops', 'flicker', 'grainFlicker', 'blotch', 'level', 'failed')})
