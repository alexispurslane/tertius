---
id: t1-tbz9
status: open
deps: []
links: []
created: 2026-11-15T13:18:54Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [rendering, lighting]
---
# Rooms get Earth's light only through one environment capture at the eye: a path trace is 16 to 58 times brighter in the cabin and in rect-lamps

Found by the reference experiment of the back-face layer (docs/research/back-face-depth-layer/stage-2/reference.md, part 1, 'The control without the sun'). A Cycles path trace of the engine's own exported frame is the reference. Under full lights, the reference's neutral floor region is 16.1 times as bright as the engine's in the cabin of demo-exit: 3.161 against 0.1961 W/(m² sr). In rect-lamps it is 58 times as bright: 1.19 against 0.02036. The reference's light there is blue. Without the sun, the factors fall to 2.5 and 2.0, so Earth's reflection of the sun is the missing light. The lead's reading: Earth's sunlit day side lights both rooms through the station's open hub and the side windows. The engine gets Earth's light only through its environment map, one capture at the eye, and the walls hide Earth from that point. Not checked: a black-Earth control for the cabin, and Earth's real texture (the reference uses Earth's mean color). Pictures: stage-2/cabin-full.png and stage-2/rect-lamps-window-full.png, last panel each. This is a design question for the lighting, outside the epic ter-kh4g.

