---
id: ter-8hk9
status: open
deps: [ter-k96l]
links: []
created: 2026-11-01T23:38:29Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, screen-space-light]
---
# The sectors pass skips the open pieces' map reads where no slice blocks a sector

Found by ter-k96l's review (2026-11-01). Since ter-k96l, the sectors pass reads the environment map along each open piece of a slice, as well as along each blocked piece, and stores 1 - S, where S = (the map's light along the open pieces) / (the map's light along all pieces). A texel where no slice blocks any sector has S exactly 1, whatever the open reads give, so those reads (about one per slice side) are wasted work. The change: keep each slice's sector mask, and read the open pieces only where some bit is blocked. It must keep the image: an open texel already stores exactly 0 for 1 - S. Measure the saving on demo-exit with interleaved runs and a control, and with two traces of every frame (tools/gpu-trace.sh --no-counters --frames 0), as ter-k96l did. Start from ter-k96l's merged code: occlusion_sectors.comp (openMapLightOf, blockedShareOf) and its C# copy in SectorSearch and AmbientEstimate.

