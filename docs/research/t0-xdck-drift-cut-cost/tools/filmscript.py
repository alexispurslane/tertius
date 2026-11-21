# Tool code of t0-xdck. filmscript.py <out.json> <case>: writes the script of the survey's film of a motion case
# (renderer-survey.py, motion_case: one frame per tick from the case's start), so that a timing run draws the same
# frames as the image take. Run it inside the dev shell from the checkout's root.
import importlib.util, json, sys
from pathlib import Path

spec = importlib.util.spec_from_file_location("survey", Path.cwd() / "tools" / "renderer-survey.py")
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
case = next(c for c in survey.load_cases()["motion"] if c["name"] == sys.argv[2])
Path(sys.argv[1]).write_text(json.dumps(survey.motion_case(case).film, indent=1) + "\n")
print(sys.argv[1], len(survey.motion_case(case).film["steps"]), "steps")
