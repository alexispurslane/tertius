# Tool code of t0-rs55. scripts.py <folder>: writes the two films of the motion take into <folder>, through the survey's own
# scene_script and walk_script (tools/renderer-survey.py), so that the card and the Mac draw the same frames:
#   turn.json: orbit's person camera after 60 ticks (the walk's view), a still (draws 0 to 32), 60 film frames standing
#     (f, draws 33 to 92), then 60 film frames (g000 to g059, draws 93 to 152) each after a mouse move of 10 px to the left
#     (PersonControl.TurnPerPixel: 0.025 rad per draw, 86 degrees in all), then 180 frames standing (h, draws 153 to 332);
#   walk.json: the survey's case cabin-walk: a still (draws 0 to 32), 180 frames standing (f, draws 33 to 212), the key W
#     held for 120 frames (g, draws 213 to 332), then 200 frames standing (h, draws 333 to 532).
import importlib.util
import json
import sys
from pathlib import Path

spec = importlib.util.spec_from_file_location("survey", Path("tools/renderer-survey.py"))
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
out = Path(sys.argv[1])
turn = []
for k in range(60):
    turn += [{"do": "post", "to": {"kind": "topic", "name": "input"}, "event": "InputEvent",
              "payload": {"kind": "mouseMove", "delta": [10, 0]}},
             {"do": "film", "name": f"g{k:03d}", "frames": 1, "every": 1}]
steps = [{"do": "capture", "name": "still"}, {"do": "film", "name": "f", "frames": 60, "every": 1}, *turn,
         {"do": "film", "name": "h", "frames": 180, "every": 1}]
(out / "turn.json").write_text(json.dumps(survey.scene_script("orbit", "person", 60, steps), indent=1))
case = next(c for c in survey.load_cases()["walks"] if c["name"] == "cabin-walk")
(out / "walk.json").write_text(json.dumps(survey.walk_script(case), indent=1))
