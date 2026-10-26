---
id: ter-qril
status: open
deps: []
links: []
created: 2026-10-26T16:18:31Z
type: bug
priority: 4
assignee: Alexis Purslane
tags: [rendering, environment-map]
---
# A frame that grows the environment maps and then throws before its submission leaves one invalid layout barrier for a map that the next, shorter chain does not draw

Found by the fixer of ter-uy7y on 2026-10-26. A frame grows the maps, for example from 3 to 4, and throws before SubmitAndPresent. The next frame serves a shorter chain, for example 3 maps. Map 3's layers are still VK_IMAGE_LAYOUT_UNDEFINED, but EnvironmentPrefilterPass.RecordMips and RecordLevels give SHADER_READ_ONLY as the old layout of every layer of a map that the frame did not draw, so that barrier is invalid once. No reader samples map 3 until the chain grows again, and then the map is refreshed, because DrawnFor is shorter. A fix that needs no new state: DrawnFor.Length == 0 exactly when no frame was submitted since the last growth, and then the prefilter gives ImageLayout.Undefined as the old layout of each undrawn map, at level 0 of the radiance maps and on the specular maps. No renderer test can make a frame throw between recording and submission today, so it needs a fault-injection hook in the renderer first.

