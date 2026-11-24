#!/usr/bin/env python3
"""Create motion (slow pan, walk) and 180-deg camera-cut scripts for Design 1 checks in orbit at tick 277003.

PersonControl.TurnPerPixel = 0.0025 rad/px (0.14324 deg/px).
"""
import json
from pathlib import Path

OUT = Path("/private/tmp/t1-wl7c/fe63-d1")
OUT.mkdir(parents=True, exist_ok=True)


def post_input(payload):
    return {"do": "post", "to": {"kind": "topic", "name": "input"}, "event": "InputEvent", "payload": payload}


# 1. Slow pan across opposite-share surfaces:
#    Start at yaw -35 deg (-244.35 px) and pitch +6 deg (-41.89 px, slightly upward to frame ceiling cone + side walls + floor).
#    Settle 60 frames, then pan right +70 deg over 240 frames (2.0362 px/frame = 0.292 deg/frame = 17.5 deg/s at 60 fps).
#    The high-share sunlit ceiling cone sweeps across screen tiles from right to center to left, alternating with share-0 walls/ceiling.
pan_steps = [
    {"do": "camera", "entity": "person"},
    {"do": "run", "ticks": 277003},
    post_input({"kind": "mouseMove", "delta": [-244.35, -41.89]}),
    {"do": "film", "name": "settle", "frames": 60, "every": 1},
]
for i in range(240):
    pan_steps.append(post_input({"kind": "mouseMove", "delta": [2.0362, 0.0]}))
    pan_steps.append({"do": "film", "name": f"p{i:03d}", "frames": 1, "every": 1})
(OUT / "pan-hall.json").write_text(json.dumps({
    "scene": "orbit", "window": {"width": 1920, "height": 1080}, "steps": pan_steps
}))

# 2. Walk forward (Key.W = 87) under the high-share sunlit ceiling cone and over the share-0 floor toward the nose window.
#    Settle 60 frames, then hold W for 180 frames (3.0 s at 60 fps).
walk_steps = [
    {"do": "camera", "entity": "person"},
    {"do": "run", "ticks": 277003},
    {"do": "film", "name": "settle", "frames": 60, "every": 1},
    post_input({"kind": "keyDown", "code": 87}),
    {"do": "film", "name": "w", "frames": 180, "every": 1},
    post_input({"kind": "keyUp", "code": 87}),
]
(OUT / "walk-hall.json").write_text(json.dumps({
    "scene": "orbit", "window": {"width": 1920, "height": 1080}, "steps": walk_steps
}))

# 3. 180-deg camera cut from settled all-share-0 aft view (delta.X = pi / 0.0025 = 1256.637 px) back to sunlit nose.
#    Settle 60 frames looking aft (all tiles drop to share 0), then instant 180-deg cut back to sunlit nose for 120 frames.
cut_steps = [
    {"do": "camera", "entity": "person"},
    {"do": "run", "ticks": 277003},
    post_input({"kind": "mouseMove", "delta": [1256.637, 0.0]}),
    {"do": "film", "name": "pre", "frames": 60, "every": 1},
    post_input({"kind": "mouseMove", "delta": [-1256.637, 0.0]}),
    {"do": "film", "name": "post", "frames": 120, "every": 1},
]
(OUT / "cut-hall.json").write_text(json.dumps({
    "scene": "orbit", "window": {"width": 1920, "height": 1080}, "steps": cut_steps
}))

print("Wrote pan-hall.json, walk-hall.json, cut-hall.json")
