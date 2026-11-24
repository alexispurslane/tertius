#!/usr/bin/env python3
"""Create real camera-cut scripts (firing RenderSnapshot.CameraCuts++ -> FrameHistory.Reset==true with DrawsOf==1)."""
import json
from pathlib import Path

OUT = Path("/private/tmp/t1-wl7c/fe63-d1/real-cuts")
OUT.mkdir(parents=True, exist_ok=True)


def post_input(payload):
    return {"do": "post", "to": {"kind": "topic", "name": "input"}, "event": "InputEvent", "payload": payload}


def make_cut_script(start_ticks: int):
    steps = [{"do": "camera", "entity": "person"}]
    if start_ticks > 0:
        steps.append({"do": "run", "ticks": start_ticks})
    # Turn 180 deg aft (pi / 0.0025 = 1256.637 px) and settle 60 frames so all tiles settle to 0.000.
    steps.append(post_input({"kind": "mouseMove", "delta": [1256.637, 0.0]}))
    steps.append({"do": "film", "name": "aft", "frames": 60, "every": 1})
    # Turn person back 180 deg toward nose during 2 sim ticks (0.0333 s, no draw) so InputEvent is delivered and applied while person is still camera.
    steps.append(post_input({"kind": "mouseMove", "delta": [-1256.637, 0.0]}))
    steps.append({"do": "run", "ticks": 2})
    # Switch camera to "far" for 1 frame (step = 3/60 = 0.050 s < 0.09 s -> DrawsOf == 1).
    steps.append({"do": "camera", "entity": "far"})
    steps.append({"do": "film", "name": "far1", "frames": 1, "every": 1})
    # Cut back to "person" (facing nose/floor): CameraCuts++ -> History.Reset == true, step = 1/60 = 0.0167 s -> DrawsOf == 1!
    steps.append({"do": "camera", "entity": "person"})
    steps.append({"do": "film", "name": "post", "frames": 65, "every": 1})
    return {"scene": "orbit", "window": {"width": 1920, "height": 1080}, "steps": steps}


(OUT / "real-cut-nose.json").write_text(json.dumps(make_cut_script(277003), indent=1))
(OUT / "real-cut-lamps.json").write_text(json.dumps(make_cut_script(0), indent=1))
print("Wrote real-cut-nose.json and real-cut-lamps.json")
